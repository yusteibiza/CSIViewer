using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Core;
using CSIViewer.Protocol;
using CSIViewer.Services;
using Windows.Storage.Streams;
using Windows.System;

namespace CSIViewer.Host.Views;

public sealed partial class RemoteControlPage : Page
{
    public static string? PendingTargetId { get; set; }
    public static string? PendingPassword { get; set; }
    public static bool PendingAutoConnect { get; set; }

    private readonly AppState _state;
    private readonly RemoteClient _client;
    private readonly ILogger<RemoteControlPage> _logger;
    private readonly Stopwatch _sessionClock = new();
    private int _pointerLogCounter;
    private int _sentMoveTraceCounter;
    private int _frames;
    private int _uiDroppedFrames;
    private double _lastRenderMilliseconds;
    private long _lastStatsTicks;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private bool _updatingMonitors;
    private readonly Dictionary<int, InputEventPayload> _pressedKeys = new();
    private VideoFrame? _pendingFrame;
    private int _renderScheduled;
    private WriteableBitmap? _writeableBitmap;
    private int _frameWidth;
    private int _frameHeight;
    private bool _isFullScreen;
    private Thickness _normalPadding;
    private long _lastMouseMoveTicks;
    private int _pageVisible;
    private int _frameGeneration;
    private bool _pointerOverToolbar;
    private long _toolbarLastActivityTicks;
    private readonly DispatcherTimer _toolbarHideTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    [StructLayout(LayoutKind.Sequential)]
    private struct ToolbarCursorPosition { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ToolbarCursorPosition point);

    public RemoteControlPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _logger = App.Services.GetRequiredService<ILogger<RemoteControlPage>>();
        _state.PropertyChanged += State_PropertyChanged;
        _toolbarHideTimer.Tick += ToolbarHideTimer_Tick;
        _client.SessionStarted += Client_SessionStarted;
        _client.SessionEnded += Client_SessionEnded;
        _client.VideoFrameReceived += Client_VideoFrameReceived;
        _client.MonitorsReceived += Client_MonitorsReceived;
        Loaded += (_, _) =>
        {
            Volatile.Write(ref _pageVisible, 1);
            _normalPadding = RootGrid.Padding;
            if (!string.IsNullOrWhiteSpace(PendingTargetId))
            {
                TargetIdBox.Text = PendingTargetId;
                PendingTargetId = null;
            }

            if (!string.IsNullOrWhiteSpace(PendingPassword))
            {
                PasswordBox.Password = PendingPassword;
                PendingPassword = null;
            }

            var autoConnect = PendingAutoConnect;
            PendingAutoConnect = false;
            UpdateSessionUi();
            if (autoConnect)
            {
                _ = ConnectAsync();
            }

            if (_state.IsSessionActive && _state.IsController)
            {
                _ = _client.RequestMonitorsAsync();
            }
        };
        Unloaded += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            Interlocked.Increment(ref _frameGeneration);
            Volatile.Write(ref _pageVisible, 0);
            Interlocked.Exchange(ref _pendingFrame, null);
            ApplyFullScreen(false);
        };
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppState.IsSessionActive) or nameof(AppState.IsController) or nameof(AppState.LastError))) return;
        void Refresh()
        {
            if (!_state.IsSessionActive || !_state.IsController)
            {
                _sessionClock.Stop();
                ApplyFullScreen(false);
            }
            UpdateSessionUi();
        }
        if (DispatcherQueue.HasThreadAccess) Refresh();
        else DispatcherQueue.TryEnqueue(Refresh);
    }

    private void Client_SessionStarted(object? sender, SessionInfo info)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            _sessionClock.Restart();
            _frames = 0;
            UpdateSessionUi();
            StatusBar.IsOpen = false;
            await _client.RequestMonitorsAsync();
        });
    }

    private void Client_MonitorsReceived(object? sender, MonitorsPayload payload)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _updatingMonitors = true;
            MonitorCombo.ItemsSource = payload.Monitors;
            var active = payload.Monitors.FirstOrDefault(x => x.Index == payload.ActiveIndex)
                ?? (payload.Monitors.Count > 0 ? payload.Monitors[0] : null);
            MonitorCombo.SelectedItem = active;
            MonitorCombo.Visibility = payload.Monitors.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            _updatingMonitors = false;
        });
    }

    private async void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMonitors || MonitorCombo.SelectedItem is not MonitorDescriptor monitor)
        {
            return;
        }

        await _client.SelectMonitorAsync(monitor.Index);
    }

    private void Client_SessionEnded(object? sender, string reason)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Cleanup for an older/non-primary session may finish after a new
            // session has started. Never clear or leave full screen for it.
            if (_state.IsSessionActive)
            {
                UpdateSessionUi();
                return;
            }
            _sessionClock.Stop();
            ReleaseAllInput();
            ApplyFullScreen(false);
            UpdateSessionUi();
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Title = global::CSIViewer.Core.Localization.Text("Sesión finalizada");
            StatusBar.Message = reason;
            StatusBar.IsOpen = true;
        });
    }

    private void Client_VideoFrameReceived(object? sender, VideoFrame frame)
    {
        if (Volatile.Read(ref _pageVisible) == 0 || !_state.IsSessionActive || !_state.IsController)
        {
            _ = Interlocked.Increment(ref _uiDroppedFrames);
            return;
        }

        // Keep only the newest frame. The render loop drains this slot on the
        // UI thread, so a slow decode never queues hundreds of dispatcher work
        // items or delays input processing.
        if (Interlocked.Exchange(ref _pendingFrame, frame) is not null)
        {
            _ = Interlocked.Increment(ref _uiDroppedFrames);
        }

        if (Interlocked.Exchange(ref _renderScheduled, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(RenderLatestFrame);
        }
    }

    private async void RenderLatestFrame()
    {
        try
        {
            // One frame per dispatcher turn. BGRA rendering is synchronous;
            // draining continuously can starve session-end and pointer events.
            if (Volatile.Read(ref _pageVisible) != 0 && _state.IsSessionActive && _state.IsController)
            {
                var frame = Interlocked.Exchange(ref _pendingFrame, null);
                if (frame is not null)
                {
                    await ShowFrameAsync(frame);
                }
            }
        }
        catch
        {
            // Malformed frames are dropped; the next frame replaces them.
        }
        finally
        {
            Volatile.Write(ref _renderScheduled, 0);
            if (Volatile.Read(ref _pageVisible) != 0 &&
                _state.IsSessionActive && _state.IsController &&
                Volatile.Read(ref _pendingFrame) is not null &&
                Interlocked.Exchange(ref _renderScheduled, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(RenderLatestFrame);
            }
        }
    }

    private async Task ShowFrameAsync(VideoFrame frame)
    {
        if (!_state.IsSessionActive || !_state.IsController || Volatile.Read(ref _pageVisible) == 0) return;
        var generation = Volatile.Read(ref _frameGeneration);
        try
        {
            var renderStarted = Stopwatch.GetTimestamp();
            if (frame.Format == VideoPixelFormat.Bgra)
            {
                if (frame.Width <= 0 || frame.Height <= 0 ||
                    frame.Data.Length < frame.Width * frame.Height * 4)
                {
                    return;
                }

                var created = false;
                if (_writeableBitmap is null ||
                    _writeableBitmap.PixelWidth != frame.Width ||
                    _writeableBitmap.PixelHeight != frame.Height)
                {
                    _writeableBitmap = new WriteableBitmap(frame.Width, frame.Height);
                    created = true;
                }

                using (var stream = _writeableBitmap.PixelBuffer.AsStream())
                {
                    stream.Position = 0;
                    stream.Write(frame.Data, 0, frame.Width * frame.Height * 4);
                }

                _writeableBitmap.Invalidate();
                if (created || !ReferenceEquals(RemoteImage.Source, _writeableBitmap))
                {
                    RemoteImage.Source = _writeableBitmap;
                }

                _frameWidth = frame.Width;
                _frameHeight = frame.Height;
            }
            else
            {
                using var stream = new InMemoryRandomAccessStream();
                var writer = new DataWriter(stream);
                writer.WriteBytes(frame.Data);
                await writer.StoreAsync();
                writer.DetachStream();
                writer.Dispose();

                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                if (generation != Volatile.Read(ref _frameGeneration) || !_state.IsSessionActive || !_state.IsController || Volatile.Read(ref _pageVisible) == 0) return;
                RemoteImage.Source = bitmap;
                _frameWidth = bitmap.PixelWidth;
                _frameHeight = bitmap.PixelHeight;
            }

            _lastRenderMilliseconds =
                Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds;
            _frames++;
            var now = Stopwatch.GetTimestamp();
            if (_lastStatsTicks == 0 ||
                Stopwatch.GetElapsedTime(_lastStatsTicks, now) > TimeSpan.FromMilliseconds(500))
            {
                var seconds = _lastStatsTicks == 0
                    ? 0.5
                    : Stopwatch.GetElapsedTime(_lastStatsTicks, now).TotalSeconds;
                var fps = _frames / Math.Max(0.001, seconds);
                _frames = 0;
                _lastStatsTicks = now;
                StatsText.Text =
                    $"{fps:0.0}/{_state.Settings.Fps} FPS · " +
                    $"{_frameWidth}x{_frameHeight} · " +
                    $"dec {_client.LastDecodeMilliseconds:0.0} ms · " +
                    $"ui {_lastRenderMilliseconds:0.0} ms · " +
                    $"{_client.DroppedVideoFrames} red · {_uiDroppedFrames} UI";
            }
        }
        catch
        {
            // A malformed or partial frame is dropped; the next frame replaces it.
        }
    }

    private void UpdateSessionUi()
    {
        RefreshFeatureControls();
        var active = _state.IsSessionActive;
        EmptyState.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        SessionView.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        if (!active)
        {
            Interlocked.Increment(ref _frameGeneration);
            _toolbarHideTimer.Stop();
            SessionToolbar.Visibility = Visibility.Visible;
            ToolbarRevealStrip.Visibility = Visibility.Collapsed;
            ReleaseAllInput();
            Interlocked.Exchange(ref _pendingFrame, null);
            _writeableBitmap = null;
            _frameWidth = 0;
            _frameHeight = 0;
            RemoteImage.Source = null;
            StatsText.Text = "";
            _updatingMonitors = true;
            MonitorCombo.ItemsSource = null;
            MonitorCombo.Visibility = Visibility.Collapsed;
            _updatingMonitors = false;
            StatsText.Visibility = Visibility.Collapsed;
            HostOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        SessionTitleText.Text = _state.IsController
            ? global::CSIViewer.Core.Localization.Format("Control de {0}",_state.RemoteDeviceName)
            : global::CSIViewer.Core.Localization.Format("Sesión remota de {0}",_state.RemoteDeviceName);
        var transport = _state.Transport switch
        {
            TransportKind.LanDirect => "LAN directa",
            _ => "Relay"
        };
        SessionDetailsText.Text = $"{PermissionNames.Describe(_state.ActivePermissions)} · {transport}";
        StatsText.Visibility = _state.IsController ? Visibility.Visible : Visibility.Collapsed;
        MonitorCombo.Visibility = _state.IsController && MonitorCombo.Items.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        MonitorCombo.IsEnabled = _state.IsController &&
                                 SessionPermissionPolicy.CanControl(_state.ActivePermissions);
        HostOverlay.Visibility = _state.IsController ? Visibility.Collapsed : Visibility.Visible;
        HostOverlayTitle.Text = global::CSIViewer.Core.Localization.Format("{0} está controlando este equipo",_state.RemoteDeviceName);
        HostOverlayDetails.Text = "Permisos: " + PermissionNames.Describe(_state.ActivePermissions);
        RemoteImage.Visibility = _state.IsController ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
        => await ConnectAsync();

    private async Task ConnectAsync()
    {
        var target = new string(TargetIdBox.Text.Where(char.IsAsciiDigit).ToArray());
        if (target.Length != 9)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = global::CSIViewer.Core.Localization.Text("ID no válido");
            StatusBar.Message = global::CSIViewer.Core.Localization.Text("Introduce los 9 dígitos del ID del equipo remoto.");
            StatusBar.IsOpen = true;
            return;
        }

        var permissions = SessionPermissions.None;
        if (ViewPermissionBox.IsChecked == true) permissions |= SessionPermissions.ViewScreen;
        if (ControlPermissionBox.IsChecked == true) permissions |= SessionPermissions.ControlInput;
        if (ChatPermissionBox.IsChecked == true) permissions |= SessionPermissions.Chat;
        if (FilePermissionBox.IsChecked == true) permissions |= SessionPermissions.SendFiles | SessionPermissions.ReceiveFiles;
        if (AudioPermissionBox.IsChecked == true) permissions |= SessionPermissions.Audio;
        if (ClipboardPermissionBox.IsChecked == true) permissions |= SessionPermissions.Clipboard;

        var chatOnly = permissions == SessionPermissions.Chat;

        if (permissions == SessionPermissions.None)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = global::CSIViewer.Core.Localization.Text("Selecciona un permiso");
            StatusBar.Message = global::CSIViewer.Core.Localization.Text("Debes solicitar al menos ver la pantalla, controlar, chat o archivos.");
            StatusBar.IsOpen = true;
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = global::CSIViewer.Core.Localization.Text("Conectando…");
        StatusBar.Message = global::CSIViewer.Core.Localization.Format("Solicitando conexión con {0}.",target);
        StatusBar.IsOpen = true;

        try
        {
            var result = await _client.ConnectToPeerAsync(
                target,
                PasswordBox.Password,
                permissions,
                chatOnly);
            if (!result.IsSuccess)
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Title = global::CSIViewer.Core.Localization.Text("No se pudo conectar");
                StatusBar.Message = result.Error?.Message ?? "Error desconocido.";
            }
            else
            {
                StatusBar.Severity = InfoBarSeverity.Success;
                StatusBar.Title = global::CSIViewer.Core.Localization.Text("Conectado");
                StatusBar.Message = global::CSIViewer.Core.Localization.Text("La sesión está activa.");
                UpdateSessionUi();
            }
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = global::CSIViewer.Core.Localization.Text("Error de conexión");
            StatusBar.Message = ex.Message;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            PasswordBox.Password = "";
        }
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        await _client.EndCurrentSessionsAsync();
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo("chat");
    }

    private void Files_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo("files");
    }

    public void LeaveFullScreen()
    {
        if (_isFullScreen)
        {
            ApplyFullScreen(false);
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        ApplyFullScreen(!_isFullScreen);
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        ReleaseAllInput();
        RemoteSurface.ReleasePointerCaptures();
        MainWindow.Instance?.MinimizeLocalWindow();
    }

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        ApplyFullScreen(false);
        MainWindow.Instance?.SetLocalWindowMaximized(true);
    }

    private void RestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        ApplyFullScreen(false);
        MainWindow.Instance?.SetLocalWindowMaximized(false);
    }

    private void ApplyFullScreen(bool enabled)
    {
        if (_isFullScreen == enabled)
        {
            return;
        }

        _isFullScreen = enabled;
        try
        {
            PageHeaderPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            ConnectionCard.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            RootGrid.Padding = enabled ? new Thickness(0) : _normalPadding;
            RootGrid.RowSpacing = enabled ? 0 : 14;
            if (enabled)
            {
                SessionCard.BorderThickness = new Thickness(0);
                SessionCard.CornerRadius = new CornerRadius(0);
            }
            else
            {
                // Restore the style/theme values, not cached colors/radii.
                SessionCard.ClearValue(Border.BorderThicknessProperty);
                SessionCard.ClearValue(Border.CornerRadiusProperty);
            }
            FullScreenButton.Content = enabled
                ? global::CSIViewer.Core.Localization.Text("Salir de pantalla completa")
                : global::CSIViewer.Core.Localization.Text("Pantalla completa");
            RemoteSurface.ReleasePointerCaptures();
            _lastMouseMoveTicks = 0;
            RemoteSurface.Focus(FocusState.Programmatic);
            MainWindow.Instance?.SetRemoteFullScreen(enabled);
            UpdateToolbarPlacement();
            PinToolbarButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            FullscreenWindowButtons.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            _pointerOverToolbar = false;
            ShowSessionToolbar();
        }
        catch
        {
            // Full screen is a convenience; input continues working in windowed mode.
        }
    }

    private void ShowSessionToolbar()
    {
        SessionToolbar.Visibility = Visibility.Visible;
        ToolbarRevealStrip.Visibility = Visibility.Collapsed;
        _toolbarHideTimer.Stop();
        _toolbarLastActivityTicks = Stopwatch.GetTimestamp();
        if (_isFullScreen && PinToolbarButton.IsChecked != true) _toolbarHideTimer.Start();
    }

    private void ToolbarHideTimer_Tick(object? sender, object e)
    {
        if (!_isFullScreen || PinToolbarButton.IsChecked == true)
        {
            _toolbarHideTimer.Stop();
            return;
        }
        var window = MainWindow.Instance;
        if (window is null || !GetCursorPos(out var cursor)) return;
        // Read-only, only in full screen. PointerEntered/Exited can be absent
        // when the pointer jumps between windows or is positioned remotely.
        // WinUI's PerMonitorV2 thread and AppWindow bounds use screen pixels.
        var position = window.AppWindow.Position;
        var size = window.AppWindow.Size;
        var atTop = cursor.X >= position.X && cursor.X < position.X + size.Width &&
                    cursor.Y >= position.Y && cursor.Y < position.Y + 6;
        if (atTop && SessionToolbar.Visibility != Visibility.Visible) ShowSessionToolbar();
        if (SessionToolbar.Visibility != Visibility.Visible) return;

        var origin = SessionToolbar.TransformToVisual(null).TransformPoint(default);
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var left = position.X + origin.X * scale;
        var top = position.Y + origin.Y * scale;
        _pointerOverToolbar = cursor.X >= left && cursor.Y >= top &&
                              cursor.X < left + SessionToolbar.ActualWidth * scale &&
                              cursor.Y < top + SessionToolbar.ActualHeight * scale;
        if (atTop || _pointerOverToolbar || MonitorCombo.IsDropDownOpen)
        {
            _toolbarLastActivityTicks = Stopwatch.GetTimestamp();
        }
        else if (Stopwatch.GetElapsedTime(_toolbarLastActivityTicks) >= TimeSpan.FromSeconds(1.5))
        {
            SessionToolbar.Visibility = Visibility.Collapsed;
            ToolbarRevealStrip.Visibility = Visibility.Visible;
        }
    }

    private void ToolbarReveal_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ShowSessionToolbar();
        e.Handled = true;
    }

    private void SessionToolbar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverToolbar = true;
        _toolbarLastActivityTicks = Stopwatch.GetTimestamp();
    }

    private void SessionToolbar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(SessionToolbar).Position;
        _pointerOverToolbar = p.X >= 0 && p.Y >= 0 && p.X < SessionToolbar.ActualWidth && p.Y < SessionToolbar.ActualHeight;
        _toolbarLastActivityTicks = Stopwatch.GetTimestamp();
        if (!_pointerOverToolbar && _isFullScreen && PinToolbarButton.IsChecked != true) _toolbarHideTimer.Start();
    }

    private void UpdateToolbarPlacement()
    {
        // Auto-hide overlays the image, so revealing/hiding never remaps the
        // desktop. Pinning explicitly reserves space to keep its top accessible.
        var overlay = _isFullScreen && PinToolbarButton.IsChecked != true;
        Grid.SetRow(RemoteViewport, overlay ? 0 : 1);
        Grid.SetRowSpan(RemoteViewport, overlay ? 2 : 1);
        Grid.SetRowSpan(SessionToolbar, overlay ? 2 : 1);
    }

    private void PinToolbar_Changed(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen) UpdateToolbarPlacement();
        ShowSessionToolbar();
    }

    private void RemoteSurface_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _logger.LogInformation(
            "Remote pointer entered. Surface={SurfaceWidth}x{SurfaceHeight} Frame={FrameWidth}x{FrameHeight} SessionActive={SessionActive} Controller={Controller} CanControlRemote={CanControlRemote}",
            RemoteSurface.ActualWidth,
            RemoteSurface.ActualHeight,
            _frameWidth,
            _frameHeight,
            _state.IsSessionActive,
            _state.IsController,
            _client.CanControlRemote);
    }

    private void RemoteSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var count = Interlocked.Increment(ref _pointerLogCounter);
        if (!CanControl())
        {
            if (count == 1 || count % 60 == 0)
            {
                _logger.LogWarning(
                    "Remote pointer ignored: control not available. SessionActive={SessionActive} Controller={Controller} CanControlRemote={CanControlRemote} Permissions={Permissions}",
                    _state.IsSessionActive,
                    _state.IsController,
                    _client.CanControlRemote,
                    _state.ActivePermissions);
            }

            return;
        }

        if (!TryGetRemotePoint(e, out var nx, out var ny))
        {
            if (count == 1 || count % 60 == 0)
            {
                _logger.LogWarning(
                    "Remote pointer outside image. Surface={SurfaceWidth}x{SurfaceHeight} Frame={FrameWidth}x{FrameHeight} Position={X},{Y}",
                    RemoteSurface.ActualWidth,
                    RemoteSurface.ActualHeight,
                    _frameWidth,
                    _frameHeight,
                    e.GetCurrentPoint(RemoteSurface).Position.X,
                    e.GetCurrentPoint(RemoteSurface).Position.Y);
            }

            return;
        }

        if (count == 1 || count % 30 == 0)
        {
            _logger.LogInformation(
                "Remote pointer moved. Normalized={NX:0.000},{NY:0.000} Frame={FrameWidth}x{FrameHeight}",
                nx,
                ny,
                _frameWidth,
                _frameHeight);
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastMouseMoveTicks != 0 &&
            Stopwatch.GetElapsedTime(_lastMouseMoveTicks, now) < TimeSpan.FromMilliseconds(8))
        {
            return;
        }

        _lastMouseMoveTicks = now;
        SendMouse("move", nx, ny, 0, 0, false);
    }

    private void RemoteSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var traceId = TracePointerEvent(e, "pressed");
        RemoteSurface.Focus(FocusState.Pointer);
        RemoteSurface.CapturePointer(e.Pointer);
        if (!CanControl() || !TryGetRemotePoint(e, out var nx, out var ny))
        {
            return;
        }

        var point = e.GetCurrentPoint(RemoteSurface);
        if (point.Properties.IsLeftButtonPressed)
        {
            _leftDown = true;
            SendMouse("leftDown", nx, ny, 0, 0, true, traceId: traceId);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            _rightDown = true;
            SendMouse("rightDown", nx, ny, 0, 0, true, traceId: traceId);
        }
        else if (point.Properties.IsMiddleButtonPressed)
        {
            _middleDown = true;
            SendMouse("middleDown", nx, ny, 0, 0, true, traceId: traceId);
        }

        e.Handled = true;
    }

    private void RemoteSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var traceId = TracePointerEvent(e, "released");
        if (!CanControl())
        {
            return;
        }

        var hasPosition = TryGetRemotePoint(e, out var nx, out var ny);
        if (_leftDown)
        {
            _leftDown = false;
            SendMouse("leftUp", nx, ny, 0, 0, true, hasPosition, traceId);
        }
        if (_rightDown)
        {
            _rightDown = false;
            SendMouse("rightUp", nx, ny, 0, 0, true, hasPosition, traceId);
        }
        if (_middleDown)
        {
            _middleDown = false;
            SendMouse("middleUp", nx, ny, 0, 0, true, hasPosition, traceId);
        }

        RemoteSurface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void RemoteSurface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var traceId = TracePointerEvent(e, "wheel");
        if (!CanControl() || !TryGetRemotePoint(e, out var nx, out var ny))
        {
            return;
        }

        var point = e.GetCurrentPoint(RemoteSurface);
        SendMouse("wheel", nx, ny, 0, point.Properties.MouseWheelDelta, true, traceId: traceId);
    }

    private void RemoteSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ReleaseAllInput();
    }

    private void RemoteSurface_LostFocus(object sender, RoutedEventArgs e)
    {
        ReleaseAllInput();
    }

    private void RemoteSurface_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!CanControl())
        {
            return;
        }

        var payload = new InputEventPayload(
            "key",
            "down",
            0,
            0,
            0,
            0,
            (int)e.Key,
            (int)e.KeyStatus.ScanCode,
            true,
            Extended: e.KeyStatus.IsExtendedKey);
        _pressedKeys[(int)e.KeyStatus.ScanCode] = payload;
        _ = _client.SendInputAsync(payload);
        e.Handled = true;
    }

    private void RemoteSurface_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!CanControl())
        {
            return;
        }

        _pressedKeys.Remove((int)e.KeyStatus.ScanCode);
        _ = _client.SendInputAsync(new InputEventPayload(
            "key",
            "up",
            0,
            0,
            0,
            0,
            (int)e.Key,
            (int)e.KeyStatus.ScanCode,
            false,
            Extended: e.KeyStatus.IsExtendedKey));
        e.Handled = true;
    }

    private void ReleaseAllInput()
    {
        if (_leftDown)
        {
            _leftDown = false;
            SendMouse("leftUp", 0, 0, 0, 0, true, hasPosition: false);
        }
        if (_rightDown)
        {
            _rightDown = false;
            SendMouse("rightUp", 0, 0, 0, 0, true, hasPosition: false);
        }
        if (_middleDown)
        {
            _middleDown = false;
            SendMouse("middleUp", 0, 0, 0, 0, true, hasPosition: false);
        }

        foreach (var key in _pressedKeys.Values.ToArray())
        {
            _ = _client.SendInputAsync(key with
            {
                Action = "up",
                Down = false,
                HasPosition = false
            });
        }

        _pressedKeys.Clear();
    }

    private bool CanControl()
        => _state.IsSessionActive &&
           _state.IsController &&
           _client.CanControlRemote;

    private bool TryGetRemotePoint(
        PointerRoutedEventArgs e,
        out double normalizedX,
        out double normalizedY)
    {
        var point = e.GetCurrentPoint(RemoteSurface);
        var sourceWidth = _frameWidth;
        var sourceHeight = _frameHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            if (MonitorCombo.SelectedItem is MonitorDescriptor monitor)
            {
                sourceWidth = monitor.Width;
                sourceHeight = monitor.Height;
            }
        }

        return RemoteViewportMapper.TryMapToNormalized(
            RemoteSurface.ActualWidth,
            RemoteSurface.ActualHeight,
            sourceWidth,
            sourceHeight,
            point.Position.X,
            point.Position.Y,
            out normalizedX,
            out normalizedY);
    }

    private string TracePointerEvent(PointerRoutedEventArgs e, string action)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var point = e.GetCurrentPoint(RemoteSurface).Position;
        var mapped = TryGetRemotePoint(e, out var nx, out var ny);
        _logger.LogInformation("Input UI event. Trace={TraceId} Action={Action} Raw={X},{Y} Surface={Width}x{Height} Frame={FrameWidth}x{FrameHeight} CanControl={CanControl} Mapped={Mapped} Normalized={NX},{NY} Source={Source}",
            traceId, action, point.X, point.Y, RemoteSurface.ActualWidth, RemoteSurface.ActualHeight,
            _frameWidth, _frameHeight, CanControl(), mapped, nx, ny, e.OriginalSource?.GetType().Name);
        return traceId;
    }

    private void SendMouse(
        string action,
        double normalizedX,
        double normalizedY,
        int button,
        int delta,
        bool buttonEvent,
        bool hasPosition = true,
        string? traceId = null)
    {
        traceId ??= Guid.NewGuid().ToString("N");
        var sampled = action != "move" || Interlocked.Increment(ref _sentMoveTraceCounter) % 30 == 1;
        var x = hasPosition ? (int)Math.Clamp(normalizedX * 65535, 0, 65535) : 0;
        var y = hasPosition ? (int)Math.Clamp(normalizedY * 65535, 0, 65535) : 0;
        if (sampled)
        {
            _logger.LogInformation("Input UI mapped. Trace={TraceId} Action={Action} x={X} y={Y} HasPosition={HasPosition} Surface={Width}x{Height} Frame={FrameWidth}x{FrameHeight}",
                traceId, action, x, y, hasPosition, RemoteSurface.ActualWidth, RemoteSurface.ActualHeight, _frameWidth, _frameHeight);
        }
        _ = _client.SendInputAsync(new InputEventPayload(
            "mouse", action, x, y, button, delta, 0, 0, buttonEvent,
            HasPosition: hasPosition, TraceId: traceId, TraceSampled: sampled));
    }
}
