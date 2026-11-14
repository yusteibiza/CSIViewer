using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Services;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace CSIViewer;

public sealed partial class MainWindow : Window
{
    private const int HostHomeHeight = 800;
    private const int HostSettingsHeight = 980;

    public static MainWindow? Instance { get; private set; }

    private readonly AppState _state;
    private readonly RemoteClient _client;
    private readonly ISettingsStore _settingsStore;
    private readonly DispatcherQueueTimer _placementSaveTimer;
    private bool _forceClose;
    private bool _remoteFullScreen;
    private bool _placementRestored;
    private bool _suppressPlacementSave;
    private bool _hasNormalRect;
    private RectInt32 _lastNormalRect;
    private int _statusUpdateScheduled;

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        TryApplyBackdrop();
        TrySetWindowIcon();
        if (App.IsHostMode)
        {
            ConfigureHostMode();
        }

        _state = App.Services.GetRequiredService<AppState>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _settingsStore = App.Services.GetRequiredService<ISettingsStore>();
        InitializeAccentMenu();
        _client.ChatMessageReceived += Client_ChatMessageReceived;

        _placementSaveTimer = DispatcherQueue.CreateTimer();
        _placementSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _placementSaveTimer.IsRepeating = false;
        _placementSaveTimer.Tick += async (_, _) => await SaveWindowPlacementAsync();

        _state.PropertyChanged += State_PropertyChanged;
        _state.SessionChanged += State_SessionChanged;
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;
        AppWindow.Closing += (_, args) =>
        {
            if (App.IsUnattended && !_forceClose)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };
        RemoteClient.MainWindowSessionBanner = message =>
        {
            DispatcherQueue.TryEnqueue(() => ShowSessionBanner(message is not null, message ?? ""));
        };

        RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        RootGrid.Loaded += (_, _) =>
        {
            ThemeService.Apply(_state.Settings.Theme, RootGrid);
            UpdateTitleBarColors();
            RestoreWindowPlacementOnce();
        };
        Closed += async (_, _) =>
        {
            try
            {
                App.Services.GetRequiredService<ILogger<MainWindow>>().LogInformation(
                    LogEvents.AppStopping,
                    "CSIViewer cerrando. HostMode={HostMode}",
                    App.IsHostMode);
            }
            catch
            {
                // Logging must not interfere with shutdown.
            }

            App.CurrentApp.DisposeTray();
            RemoteClient.MainWindowSessionBanner = null;
            try
            {
                await _client.DisposeAsync();
            }
            catch
            {
                // Best effort during shutdown.
            }
        };

        if (App.IsHostMode)
        {
            NavView.SelectedItem = null;
            Navigate("host");
        }
        else
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            Navigate("home");
        }

        UpdateStatus();
        RestoreWindowPlacementOnce();
    }

    /// <summary>
    /// Presents the same process as the machine being controlled. The host
    /// needs only the ID/password page; the client-side sections are removed
    /// and the navigation pane stays collapsed.
    /// </summary>
    private void ConfigureHostMode()
    {
        Title = "CSIViewer Host";
        AppTitleText.Text = "CSIViewer Host";
        TopBarHintText.Text = global::CSIViewer.Core.Localization.Text("Equipo anfitrión");
        NavView.Visibility = Visibility.Collapsed;
        HostFrame.Visibility = Visibility.Visible;

        var clientOnlyTags = new[]
        {
            "remote",
            "devices",
            "files",
            "chat",
            "history",
            "contacts"
        };

        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>().ToList())
        {
            if (item.Tag is string tag && clientOnlyTags.Contains(tag))
            {
                NavView.MenuItems.Remove(item);
            }
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Appicon.ico");
            if (File.Exists(icon))
            {
                AppWindow.SetIcon(icon);
            }
        }
        catch
        {
            // The embedded executable icon remains as fallback.
        }
    }

    private void TryApplyBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        }
        catch
        {
            // A solid background is a valid fallback.
        }
    }

    private void RestoreWindowPlacementOnce(bool settingsLoaded = false)
    {
        if (_placementRestored)
        {
            return;
        }

        if (App.IsHostMode)
        {
            _placementRestored = true;
            ApplyHostWindowSize();
            return;
        }

        var settings = _state.Settings;
        var hasPlacement = settings.WindowWidth >= 400 && settings.WindowHeight >= 300;
        if (!hasPlacement && !settingsLoaded && _state.Identity is null)
        {
            return;
        }

        _placementRestored = true;
        if (!hasPlacement)
        {
            try
            {
                var size = AppWindow.Size;
                var position = AppWindow.Position;
                if (size.Width >= 400 && size.Height >= 300)
                {
                    _lastNormalRect = new RectInt32(
                        position.X,
                        position.Y,
                        size.Width,
                        size.Height);
                    _hasNormalRect = true;
                }
            }
            catch
            {
                // The window may not be fully initialized yet.
            }

            return;
        }

        _suppressPlacementSave = true;
        try
        {
            var rect = ClampToVisibleArea(
                settings.WindowX,
                settings.WindowY,
                settings.WindowWidth,
                settings.WindowHeight);
            AppWindow.MoveAndResize(rect);
            _lastNormalRect = rect;
            _hasNormalRect = true;
            if (settings.WindowMaximized &&
                AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch
        {
            // A default placement is still a valid fallback.
        }
        finally
        {
            _suppressPlacementSave = false;
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_placementRestored || _suppressPlacementSave)
        {
            return;
        }

        if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
        {
            _placementSaveTimer.Stop();
            _placementSaveTimer.Start();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _placementSaveTimer.Stop();
        SaveWindowPlacementNow();
    }

    /// <summary>
    /// Captures the last normal (non-maximized) rectangle and persists the
    /// placement. Safe to call from the tray exit path, which does not
    /// necessarily raise AppWindow.Closed.
    /// </summary>
    public void SaveWindowPlacementNow()
    {
        if (!UpdatePlacementSettings())
        {
            return;
        }

        try
        {
            _settingsStore.SaveAsync(_state.Settings)
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Placement persistence is best effort.
        }
    }

    private async Task SaveWindowPlacementAsync()
    {
        if (!UpdatePlacementSettings())
        {
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(_state.Settings).ConfigureAwait(false);
        }
        catch
        {
            // Placement persistence is best effort.
        }
    }

    private bool UpdatePlacementSettings()
    {
        if (!TryCapturePlacement(out var rect, out var maximized))
        {
            return false;
        }

        var settings = _state.Settings;
        settings.WindowX = rect.X;
        settings.WindowY = rect.Y;
        settings.WindowWidth = rect.Width;
        settings.WindowHeight = rect.Height;
        settings.WindowMaximized = maximized;
        return true;
    }

    private bool TryCapturePlacement(out RectInt32 rect, out bool maximized)
    {
        rect = default;
        maximized = false;
        if (!_placementRestored || App.IsHostMode)
        {
            return false;
        }

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        var state = presenter?.State;
        maximized = state == OverlappedPresenterState.Maximized;

        if (state == OverlappedPresenterState.Restored)
        {
            var position = AppWindow.Position;
            var size = AppWindow.Size;
            if (size.Width >= 400 && size.Height >= 300)
            {
                _lastNormalRect = new RectInt32(
                    position.X,
                    position.Y,
                    size.Width,
                    size.Height);
                _hasNormalRect = true;
            }
        }

        if (!_hasNormalRect)
        {
            return false;
        }

        rect = _lastNormalRect;
        return true;
    }

    private void ApplyHostWindowSize()
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var area = display.WorkArea;
            var width = Math.Min(780, Math.Max(560, area.Width - 80));
            var height = Math.Min(HostHomeHeight, Math.Max(540, area.Height - 80));
            var left = area.X + Math.Max(0, (area.Width - width) / 2);
            var top = area.Y + Math.Max(0, (area.Height - height) / 2);
            AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        }
        catch
        {
            // The window can keep its default size if the display is unavailable.
        }
    }

    private void ResizeHostWindow(int height)
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var area = display.WorkArea;
            var width = Math.Min(780, Math.Max(560, area.Width - 80));
            var targetHeight = Math.Min(height, Math.Max(540, area.Height - 40));
            var position = AppWindow.Position;
            var left = Math.Clamp(
                position.X,
                area.X,
                area.X + Math.Max(0, area.Width - width));
            var top = Math.Clamp(
                position.Y,
                area.Y,
                area.Y + Math.Max(0, area.Height - targetHeight));
            AppWindow.MoveAndResize(new RectInt32(left, top, width, targetHeight));
        }
        catch
        {
            // The current size remains usable.
        }
    }

    private static RectInt32 ClampToVisibleArea(int x, int y, int width, int height)
    {
        var display = DisplayArea.GetFromPoint(
            new PointInt32(x, y),
            DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        var minWidth = Math.Min(640, Math.Max(320, area.Width));
        var minHeight = Math.Min(480, Math.Max(240, area.Height));
        var clampedWidth = Math.Clamp(width, minWidth, Math.Max(minWidth, area.Width));
        var clampedHeight = Math.Clamp(height, minHeight, Math.Max(minHeight, area.Height));
        var left = Math.Clamp(
            x,
            area.X,
            area.X + Math.Max(0, area.Width - clampedWidth));
        var top = Math.Clamp(
            y,
            area.Y,
            area.Y + Math.Max(0, area.Height - clampedHeight));
        return new RectInt32(left, top, clampedWidth, clampedHeight);
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Settings))
        {
            DispatcherQueue.TryEnqueue(() => RestoreWindowPlacementOnce(settingsLoaded: true));
            return;
        }

        if (e.PropertyName is nameof(AppState.DeviceId) or nameof(AppState.ServerStatus) or
            nameof(AppState.IsServerOnline) or nameof(AppState.IsSessionActive) or
            nameof(AppState.RemoteDeviceName) or nameof(AppState.StatusMessage))
        {
            ScheduleStatusUpdate();
        }
    }

    private void State_SessionChanged(object? sender, EventArgs e)
    {
        ScheduleStatusUpdate();
    }

    private void ScheduleStatusUpdate()
    {
        if (Interlocked.Exchange(ref _statusUpdateScheduled, 1) != 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _statusUpdateScheduled, 0);
            UpdateStatus();
        });
    }

    /// <summary>
    /// Hides the application chrome while the remote image is full screen.
    /// The remote page owns the auto-hiding, pinnable session toolbar.
    /// </summary>
    public void SetRemoteFullScreen(bool enabled)
    {
        if (_remoteFullScreen == enabled)
        {
            return;
        }

        _remoteFullScreen = enabled;
        try
        {
            AppTitleBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            StatusBarRow.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            SessionBanner.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            NavView.IsPaneVisible = !enabled && !App.IsHostMode;
            NavView.IsTitleBarAutoPaddingEnabled = !enabled;
            RootGrid.RowDefinitions[0].Height = enabled ? new GridLength(0) : new GridLength(40);
            RootGrid.RowDefinitions[1].Height = enabled ? new GridLength(0) : new GridLength(44);
            RootGrid.RowDefinitions[2].Height = enabled ? new GridLength(0) : GridLength.Auto;
            AppWindow.SetPresenter(
                enabled ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        }
        catch
        {
            // Full screen is a convenience; the session remains usable.
        }
    }

    public void MinimizeLocalWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
        else
        {
            // FullScreenPresenter has no Minimize API. Minimize the same HWND
            // without leaving full screen so taskbar restore keeps that mode.
            _ = ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), 6);
        }
    }

    public void SetLocalWindowMaximized(bool maximized)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.IsResizable = true;
        presenter.IsMaximizable = true;
        presenter.IsMinimizable = true;
        if (maximized) presenter.Maximize();
        else presenter.Restore();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    private void UpdateTitleBarColors()
    {
        try
        {
            var isDark = RootGrid.ActualTheme == ElementTheme.Dark ||
                         (RootGrid.ActualTheme == ElementTheme.Default &&
                          Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var titleBar = AppWindow.TitleBar;
            var foreground = isDark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = isDark
                ? Color.FromArgb(150, 255, 255, 255)
                : Color.FromArgb(150, 32, 32, 32);
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(32, 255, 255, 255)
                : Color.FromArgb(20, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(48, 255, 255, 255)
                : Color.FromArgb(30, 0, 0, 0);
        }
        catch
        {
            // Custom caption colors are cosmetic; keep the window usable.
        }
    }

    private void Client_ChatMessageReceived(object? sender, ChatMessage message)
    {
        if (message.IsMine)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(ShowChatForIncomingMessage);
    }

    /// <summary>
    /// Brings the window to the front when a chat message arrives and opens
    /// the Chat page, except while the user is actively controlling a remote
    /// screen in the foreground (that view must not be interrupted).
    /// </summary>
    public void ShowForConsent()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        if (!AppWindow.IsVisible) AppWindow.Show();
        Activate();
    }

    private void ShowChatForIncomingMessage()
    {
        try
        {
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            var minimized = presenter?.State == OverlappedPresenterState.Minimized;
            var hidden = !AppWindow.IsVisible;

            if (minimized)
            {
                presenter!.Restore();
            }

            if (hidden)
            {
                AppWindow.Show();
            }

            Activate();

            if (App.IsHostMode)
            {
                if (HostFrame.CurrentSourcePageType != typeof(Views.ChatPage))
                {
                    Navigate("chat", updateSelection: false);
                }

                return;
            }

            var onRemoteControl =
                ContentFrame.CurrentSourcePageType == typeof(Views.RemoteControlPage);
            if (onRemoteControl && !minimized && !hidden)
            {
                return;
            }

            if (onRemoteControl && ContentFrame.Content is Views.RemoteControlPage remote)
            {
                remote.LeaveFullScreen();
            }

            if (ContentFrame.CurrentSourcePageType != typeof(Views.ChatPage))
            {
                NavigateTo("chat");
            }
        }
        catch
        {
            // A notification must never break the session.
        }
    }

    private void UpdateStatus()
    {
        SetServerStatus(_state.ServerStatus, _state.IsServerOnline);
        SetDeviceId(_state.DeviceId);
        if (_state.IsSessionActive)
        {
            SessionBanner.Message = _state.IsController
                ? global::CSIViewer.Core.Localization.Format("Sesión de control con {0}.",_state.RemoteDeviceName)
                : global::CSIViewer.Core.Localization.Format("Sesión remota de {0}. Puedes finalizarla cuando quieras.",_state.RemoteDeviceName);
            SessionBanner.IsOpen = true;
        }
        else
        {
            SessionBanner.IsOpen = false;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag, updateSelection: false);
        }
    }

    public void AllowClose() => _forceClose = true;

    public void HideToTray() => AppWindow.Hide();

    public void NavigateTo(string tag)
    {
        if (App.IsHostMode)
        {
            Navigate(tag, updateSelection: false);
            return;
        }

        foreach (var item in NavView.MenuItems.Concat(NavView.FooterMenuItems))
        {
            if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
            {
                NavView.SelectedItem = navItem;
                Navigate(tag, updateSelection: false);
                return;
            }
        }
    }

    private void Navigate(string tag, bool updateSelection = true)
    {
        var pageType = tag switch
        {
            "home" => typeof(Views.HomePage),
            "host" => typeof(Views.HostPage),
            "host-settings" => typeof(Views.HostSettingsPage),
            "remote" => typeof(Views.RemoteControlPage),
            "devices" => typeof(Views.DevicesPage),
            "files" => typeof(Views.FileTransferPage),
            "chat" => typeof(Views.ChatPage),
            "history" => typeof(Views.HistoryPage),
            "contacts" => typeof(Views.ContactsPage),
            "settings" => App.IsHostMode ? typeof(Views.HostSettingsPage) : typeof(Views.SettingsPage),
            "help" => typeof(Views.HelpPage),
            "about" => typeof(Views.AboutPage),
            _ => typeof(Views.HomePage)
        };

        if (App.IsHostMode)
        {
            if (HostFrame.CurrentSourcePageType != pageType)
            {
                HostFrame.Navigate(pageType);
            }

            HostBackButton.Visibility = tag == "host" ? Visibility.Collapsed : Visibility.Visible;
            ResizeHostWindow(tag == "host-settings" ? HostSettingsHeight : HostHomeHeight);
            return;
        }

        if (updateSelection)
        {
            NavigateTo(tag);
            return;
        }

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void HostBack_Click(object sender, RoutedEventArgs e)
    {
        Navigate("host", updateSelection: false);
    }

    private async void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tag)
        {
            var theme = tag switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.System
            };

            ThemeService.Apply(theme, RootGrid);
            UpdateTitleBarColors();
            _state.Settings.Theme = theme;
            try
            {
                await _settingsStore.SaveAsync(_state.Settings);
            }
            catch
            {
                // The theme is already applied; persistence can be retried later.
            }
        }
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        await _client.EndCurrentSessionsAsync();
    }

    public void SetServerStatus(string text, bool online)
    {
        ServerStatusText.Text = text;
        TopStatusDot.Fill = (Brush)XamlApplication.Current.Resources[online ? "OnlineBrush" : "OfflineBrush"];
    }

    public void SetDeviceId(string deviceId)
    {
        DeviceIdHeaderText.Text = deviceId == "—" ? global::CSIViewer.Core.Localization.Text("ID —") : $"ID {deviceId}";
    }

    public void ShowSessionBanner(bool visible, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            SessionBanner.Message = message;
        }

        SessionBanner.IsOpen = visible;
    }
}
