using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using CSIViewer.Application;
using CSIViewer.Infrastructure.Logging;

namespace CSIViewer.Server;

public sealed partial class MainWindow : Window
{
    private readonly ServerRuntimeSettings _settings;
    private readonly ServerHost _host = new();
    private readonly DispatcherQueueTimer _placementSaveTimer;
    private ServerTrayIcon? _tray;
    private bool _busy;
    private bool _loaded;
    private bool _suppressThemeChange;
    private bool _exitRequested;
    private bool _hasNormalRect;
    private RectInt32 _lastNormalRect;
    private AppLogLevel _selectedLogLevel = AppLogLevel.Information;
    private readonly ServerServiceManager _serviceManager = new();
    private ServerServiceStatus? _serviceStatus;
    private bool _serviceBusy;

    public MainWindow(ServerCommandLine commandLine)
    {
        _settings = commandLine.Settings.Clone();
        _settings.ExplicitUrl = null;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
        TrySetWindowIcon();

        _placementSaveTimer = DispatcherQueue.CreateTimer();
        _placementSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _placementSaveTimer.IsRepeating = false;
        _placementSaveTimer.Tick += async (_, _) => await SaveWindowPlacementAsync();
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;

        RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        RootGrid.Loaded += async (_, _) => await InitializeAsync();
        _tray = new ServerTrayIcon(ShowFromTray, ExitFromTray, StartServerAsync, StopServerAsync);
        _tray.Initialize();
        AppWindow.Closing += (_, args) =>
        {
            if (_exitRequested || _tray?.IsInitialized != true)
            {
                return;
            }

            args.Cancel = true;
            AppWindow.Hide();
            _tray.SetRunning(_host.IsRunning);
        };
        Closed += async (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
            try
            {
                await _host.DisposeAsync();
            }
            catch
            {
                // Best effort during shutdown.
            }
        };
    }

    private async Task InitializeAsync()
    {
        if (_loaded)
        {
            return;
        }

        PortBox.Value = _settings.Port;
        AllInterfacesSwitch.IsOn = _settings.ListenOnAllInterfaces;
        EnrollmentCodeBox.Text = _settings.EnrollmentCode;
        ServerNameBox.Text = _settings.ServerName;
        DatabasePathBox.Text = _settings.DatabasePath;
        UpnpSwitch.IsOn = _settings.EnableUpnp;
        UpnpExternalPortBox.Value = _settings.UpnpExternalPort;
        HttpsSwitch.IsOn = _settings.UseHttps;
        CertificateThumbprintBox.Text = _settings.CertificateThumbprint;
        StunSwitch.IsOn = _settings.EnableStunServer;
        StunPortBox.Value = _settings.StunPort;
        MaxSessionsPerHostBox.Value = _settings.MaxSessionsPerHost;
        _settings.StartWithWindows = ServerStartup.IsEnabled;
        StartWithWindowsSwitch.IsOn = _settings.StartWithWindows;
        AutoStartSwitch.IsOn = _settings.AutoStartWithUi;
        ApplyLogLevelSelection(_settings.LogLevel);

        _suppressThemeChange = true;
        AccentColorCombo.ItemsSource = CSIViewer.Core.AccentPalette.Choices;
        LanguageCombo.ItemsSource = CSIViewer.Core.Localization.Languages;
        LanguageCombo.SelectedItem = CSIViewer.Core.Localization.Languages.First(x => x.Code == _settings.Language);
        AccentColorCombo.SelectedItem = CSIViewer.Core.AccentPalette.Choices.First(x => x.Value == _settings.AccentColor);
        ThemeCombo.SelectedIndex = _settings.Theme switch
        {
            ServerTheme.Light => 1,
            ServerTheme.Dark => 2,
            _ => 0
        };
        _suppressThemeChange = false;
        ApplyTheme(_settings.Theme);
        UpdateStatus();
        var serviceStatus = await RefreshServiceStatusAsync();
        _loaded = true;

        if (serviceStatus.Running)
        {
            ShowStatus(
                InfoBarSeverity.Informational,
                global::CSIViewer.Core.Localization.Text("Servidor en ejecución como servicio"),
                global::CSIViewer.Core.Localization.Text("El servicio de Windows ya está ejecutando el servidor. Esta ventana solo sirve para ") +
                global::CSIViewer.Core.Localization.Text("administrarlo; no se iniciará un segundo servidor."));
        }
        else if (_settings.AutoStartWithUi)
        {
            if (IsAnotherServerProcessRunning())
            {
                ShowStatus(
                    InfoBarSeverity.Warning,
                    global::CSIViewer.Core.Localization.Text("Servidor ya en ejecución"),
                    global::CSIViewer.Core.Localization.Text("Se ha detectado otra instancia del servidor (consola o servicio). ") +
                    global::CSIViewer.Core.Localization.Text("Esta ventana no iniciará un segundo servidor; puedes revisar la configuración ") +
                    global::CSIViewer.Core.Localization.Text("o cerrar la otra instancia antes de pulsar «Iniciar servidor»."));
            }
            else
            {
                await StartServerAsync();
            }
        }
    }

    private static bool IsAnotherServerProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("CSIViewerServidor")
                .Any(process => process.Id != Environment.ProcessId);
        }
        catch
        {
            return false;
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

    private void ConfigureWindow()
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var area = display.WorkArea;
            if (_settings.WindowWidth >= 400 && _settings.WindowHeight >= 300)
            {
                var targetDisplay = DisplayArea.GetFromPoint(
                    new PointInt32(_settings.WindowX, _settings.WindowY),
                    DisplayAreaFallback.Nearest);
                area = targetDisplay.WorkArea;
                var minWidth = Math.Min(640, Math.Max(320, area.Width));
                var minHeight = Math.Min(480, Math.Max(240, area.Height));
                var width = Math.Clamp(
                    _settings.WindowWidth,
                    minWidth,
                    Math.Max(minWidth, area.Width));
                var height = Math.Clamp(
                    _settings.WindowHeight,
                    minHeight,
                    Math.Max(minHeight, area.Height));
                var x = Math.Clamp(
                    _settings.WindowX,
                    area.X,
                    area.X + Math.Max(0, area.Width - width));
                var y = Math.Clamp(
                    _settings.WindowY,
                    area.Y,
                    area.Y + Math.Max(0, area.Height - height));
                var saved = new RectInt32(x, y, width, height);
                AppWindow.MoveAndResize(saved);
                _lastNormalRect = saved;
                _hasNormalRect = true;
                if (_settings.WindowMaximized &&
                    AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }

                return;
            }

            var defaultWidth = Math.Min(1000, Math.Max(760, area.Width - 100));
            var defaultHeight = Math.Min(860, Math.Max(620, area.Height - 100));
            var defaultX = area.X + (area.Width - defaultWidth) / 2;
            var defaultY = area.Y + (area.Height - defaultHeight) / 2;
            var defaultRect = new RectInt32(defaultX, defaultY, defaultWidth, defaultHeight);
            AppWindow.MoveAndResize(defaultRect);
            _lastNormalRect = defaultRect;
            _hasNormalRect = true;
        }
        catch
        {
            var fallback = new RectInt32(0, 0, 1000, 820);
            AppWindow.Resize(new SizeInt32(1000, 820));
            _lastNormalRect = fallback;
            _hasNormalRect = true;
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
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

    private void SaveWindowPlacementNow()
    {
        if (!UpdatePlacementSettings())
        {
            return;
        }

        try
        {
            ServerSettingsStore.Save(_settings);
        }
        catch
        {
            // Placement persistence is best effort.
        }
    }

    private Task SaveWindowPlacementAsync()
    {
        if (!UpdatePlacementSettings())
        {
            return Task.CompletedTask;
        }

        try
        {
            ServerSettingsStore.Save(_settings);
        }
        catch
        {
            // Placement persistence is best effort.
        }

        return Task.CompletedTask;
    }

    private bool UpdatePlacementSettings()
    {
        if (!TryCapturePlacement(out var rect, out var maximized))
        {
            return false;
        }

        _settings.WindowX = rect.X;
        _settings.WindowY = rect.Y;
        _settings.WindowWidth = rect.Width;
        _settings.WindowHeight = rect.Height;
        _settings.WindowMaximized = maximized;
        return true;
    }

    private bool TryCapturePlacement(out RectInt32 rect, out bool maximized)
    {
        rect = default;
        maximized = false;
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

    private void ApplyTheme(ServerTheme theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            ServerTheme.Light => ElementTheme.Light,
            ServerTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        CSIViewer.Presentation.AccentAppearance.Apply(_settings.AccentColor, RootGrid);
        CSIViewer.Presentation.Translate.Apply(RootGrid, _settings.Language);
        UpdateTitleBarColors();
    }

    private void UpdateTitleBarColors()
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

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || !_loaded)
        {
            return;
        }

        var tag = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _settings.Theme = tag switch
        {
            "Light" => ServerTheme.Light,
            "Dark" => ServerTheme.Dark,
            _ => ServerTheme.System
        };
        ApplyTheme(_settings.Theme);
        SaveAppearance();
    }

    private void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || !_loaded || AccentColorCombo.SelectedItem is not CSIViewer.Core.AccentChoice choice) return;
        _settings.AccentColor = choice.Value;
        CSIViewer.Presentation.AccentAppearance.Apply(choice.Value, RootGrid);
        SaveAppearance();
    }

    private void SaveAppearance()
    {
        try
        {
            // Appearance never rewrites startup registration or applies pending
            // port/network/service edits from the rest of the window.
            ServerSettingsStore.Save(_settings);
            SaveHint.Text = global::CSIViewer.Core.Localization.Format("Apariencia guardada a las {0:HH:mm}",DateTime.Now);
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Warning, global::CSIViewer.Core.Localization.Text("Apariencia"), global::CSIViewer.Core.Localization.Text("No se pudo guardar: ") + ex.Message); }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || !_loaded || LanguageCombo.SelectedItem is not CSIViewer.Core.LanguageChoice choice) return;
        _settings.Language = choice.Code;
        CSIViewer.Presentation.Translate.Apply(RootGrid, choice.Code);
        SaveAppearance();
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        await StartServerAsync();
    }

    private async Task StartServerAsync()
    {
        if (_busy || _host.IsRunning)
        {
            return;
        }

        if (_serviceStatus?.Running == true)
        {
            ShowStatus(
                InfoBarSeverity.Warning,
                global::CSIViewer.Core.Localization.Text("Servicio en ejecución"),
                global::CSIViewer.Core.Localization.Text("El servidor ya está ejecutándose como servicio de Windows. Para el servicio o ") +
                global::CSIViewer.Core.Localization.Text("elimínalo antes de iniciar el servidor desde esta ventana."));
            return;
        }

        if (!ReadSettingsFromUi(out var validationError))
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Configuración no válida"), validationError);
            return;
        }

        _busy = true;
        UpdateStatus();
        try
        {
            ServerSettingsStore.Save(_settings);
            await _host.StartAsync(_settings);

            var upnpMessage = string.Join(" ", _host.UpnpMessages);
            if (_host.UpnpFailed)
            {
                ShowStatus(InfoBarSeverity.Warning,
                    global::CSIViewer.Core.Localization.Text("Servidor iniciado; UPnP no disponible"),
                    global::CSIViewer.Core.Localization.Format("Escuchando en {0}. {1}",_host.ActiveUrl,upnpMessage));
            }
            else if (_host.UpnpMessages.Count > 0)
            {
                ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Servidor iniciado"),
                    global::CSIViewer.Core.Localization.Format("Escuchando en {0}. {1}",_host.ActiveUrl,upnpMessage));
            }
            else
            {
                ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Servidor iniciado"),
                    global::CSIViewer.Core.Localization.Format("Escuchando en {0}.",_host.ActiveUrl));
            }
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo iniciar el servidor"),
                ex.GetBaseException().Message);
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private async void StopServer_Click(object sender, RoutedEventArgs e) => await StopServerAsync();

    private async Task StopServerAsync()
    {
        if (_busy || !_host.IsRunning)
        {
            return;
        }

        _busy = true;
        UpdateStatus();
        try
        {
            await _host.StopAsync();
            ShowStatus(InfoBarSeverity.Informational, global::CSIViewer.Core.Localization.Text("Servidor detenido"),
                global::CSIViewer.Core.Localization.Text("Las conexiones existentes se han cerrado."));
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo detener el servidor"),
                ex.GetBaseException().Message);
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private async Task<ServerServiceStatus> RefreshServiceStatusAsync()
    {
        var status = await _serviceManager.GetStatusAsync();
        _serviceStatus = status;
        ServiceRunningDot.Visibility = status.Running ? Visibility.Visible : Visibility.Collapsed;
        ServiceStoppedDot.Visibility = status.Running ? Visibility.Collapsed : Visibility.Visible;
        ServiceStatusText.Text = status.Installed
            ? $"Servicio instalado: {status.Description.ToLowerInvariant()}"
            : global::CSIViewer.Core.Localization.Text("Servicio no instalado. El servidor solo se ejecuta desde esta ventana.");
        CreateServiceButton.IsEnabled = !_serviceBusy && !status.Installed;
        DeleteServiceButton.IsEnabled = !_serviceBusy && status.Installed;
        StartServiceButton.IsEnabled = !_serviceBusy && status.Installed && !status.Running;
        StopServiceButton.IsEnabled = !_serviceBusy && status.Installed && status.Running;
        RefreshServiceButton.IsEnabled = !_serviceBusy;
        UpdateStatus();
        return status;
    }

    private void SetServiceBusy(bool busy)
    {
        _serviceBusy = busy;
        ServiceBusyRing.IsActive = busy;
        ServiceBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CreateServiceButton.IsEnabled = !busy && _serviceStatus?.Installed != true;
        DeleteServiceButton.IsEnabled = !busy && _serviceStatus?.Installed == true;
        StartServiceButton.IsEnabled = !busy && _serviceStatus?.Installed == true && _serviceStatus?.Running != true;
        StopServiceButton.IsEnabled = !busy && _serviceStatus?.Installed == true && _serviceStatus?.Running == true;
        RefreshServiceButton.IsEnabled = !busy;
    }

    private async void RefreshService_Click(object sender, RoutedEventArgs e) =>
        await RefreshServiceStatusAsync();

    private async void CreateService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceBusy)
        {
            return;
        }

        if (!ReadSettingsFromUi(out var validationError))
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Configuración no válida"), validationError);
            return;
        }

        if (_host.IsRunning)
        {
            var stop = await ConfirmAsync(
                global::CSIViewer.Core.Localization.Text("Instalar el servicio"),
                global::CSIViewer.Core.Localization.Text("El servidor que está ejecutando esta ventana se detendrá para que el servicio ") +
                global::CSIViewer.Core.Localization.Text("pueda usar el mismo puerto. ¿Quieres continuar?"));
            if (!stop)
            {
                return;
            }

            await StopServerAsync();
        }

        SetServiceBusy(true);
        try
        {
            ShowStatus(
                InfoBarSeverity.Informational,
                global::CSIViewer.Core.Localization.Text("Instalando el servicio"),
                global::CSIViewer.Core.Localization.Text("Acepta el aviso de Windows para conceder permisos de administrador."));
            var result = await _serviceManager.InstallAsync(_settings);
            ShowStatus(
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                result.Success ? global::CSIViewer.Core.Localization.Text("Servicio instalado y en ejecución") : global::CSIViewer.Core.Localization.Text("No se pudo instalar el servicio"),
                result.Message);

            if (result.Success)
            {
                // The service now starts with Windows, so the per-user startup
                // shortcut would launch a second server at sign-in or open the
                // UI unnecessarily.
                StartWithWindowsSwitch.IsOn = false;
                AutoStartSwitch.IsOn = false;
                _settings.StartWithWindows = false;
                _settings.AutoStartWithUi = false;
                SaveSettings(silent: true);
            }

            await RefreshServiceStatusAsync();
        }
        finally
        {
            SetServiceBusy(false);
        }
    }

    private async void DeleteService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceBusy)
        {
            return;
        }

        var confirm = await ConfirmAsync(
            global::CSIViewer.Core.Localization.Text("Quitar el servicio"),
            global::CSIViewer.Core.Localization.Text("Se detendrá y eliminará el servicio de Windows. El servidor seguirá disponible ") +
            global::CSIViewer.Core.Localization.Text("desde esta ventana. ¿Quieres continuar?"));
        if (!confirm)
        {
            return;
        }

        SetServiceBusy(true);
        try
        {
            var result = await _serviceManager.UninstallAsync();
            ShowStatus(
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                result.Success ? "Servicio eliminado" : global::CSIViewer.Core.Localization.Text("No se pudo quitar el servicio"),
                result.Message);
            await RefreshServiceStatusAsync();
        }
        finally
        {
            SetServiceBusy(false);
        }
    }

    private async void StartService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceBusy || _serviceStatus?.Installed != true)
        {
            return;
        }

        if (_host.IsRunning)
        {
            var stopLocal = await ConfirmAsync(
                global::CSIViewer.Core.Localization.Text("Iniciar el servicio"),
                global::CSIViewer.Core.Localization.Text("El servidor que está ejecutando esta ventana se detendrá para que el servicio ") +
                global::CSIViewer.Core.Localization.Text("pueda usar el mismo puerto. ¿Quieres continuar?"));
            if (!stopLocal)
            {
                return;
            }

            await StopServerAsync();
        }

        SetServiceBusy(true);
        try
        {
            var result = await _serviceManager.StartAsync();
            ShowStatus(
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                result.Success ? "Servicio iniciado" : global::CSIViewer.Core.Localization.Text("No se pudo iniciar el servicio"),
                result.Message);
            await RefreshServiceStatusAsync();
        }
        finally
        {
            SetServiceBusy(false);
        }
    }

    private async void StopService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceBusy || _serviceStatus?.Installed != true)
        {
            return;
        }

        var confirm = await ConfirmAsync(
            global::CSIViewer.Core.Localization.Text("Parar el servicio"),
            global::CSIViewer.Core.Localization.Text("Se detendrá el servicio de Windows y se cerrarán las conexiones que esté atendiendo. ") +
            global::CSIViewer.Core.Localization.Text("El servidor seguirá disponible desde esta ventana. ¿Quieres continuar?"));
        if (!confirm)
        {
            return;
        }

        SetServiceBusy(true);
        try
        {
            var result = await _serviceManager.StopAsync();
            ShowStatus(
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                result.Success ? "Servicio detenido" : global::CSIViewer.Core.Localization.Text("No se pudo parar el servicio"),
                result.Message);
            await RefreshServiceStatusAsync();
        }
        finally
        {
            SetServiceBusy(false);
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Continuar",
            CloseButtonText = global::CSIViewer.Core.Localization.Text("Cancelar"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot,
            Title = global::CSIViewer.Core.Localization.Text("Borrar historial de sesiones"),
            Content = global::CSIViewer.Core.Localization.Text("Se eliminarán permanentemente los registros de sesiones guardados en la base de datos ") +
                      global::CSIViewer.Core.Localization.Text("del servidor. Los equipos registrados, el código de inscripción y las sesiones activas ") +
                      global::CSIViewer.Core.Localization.Text("no se modifican. ¿Quieres continuar?"),
            PrimaryButtonText = global::CSIViewer.Core.Localization.Text("Borrar historial"),
            CloseButtonText = global::CSIViewer.Core.Localization.Text("Cancelar"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var removed = await _host.PurgeSessionHistoryAsync(_settings.DatabasePath);
            ShowStatus(
                InfoBarSeverity.Success,
                "Historial borrado",
                removed == 0
                    ? global::CSIViewer.Core.Localization.Text("No había sesiones guardadas en la base de datos del servidor.")
                    : global::CSIViewer.Core.Localization.Format("Se han eliminado {0} registros de sesiones.",removed));
        }
        catch (Exception ex)
        {
            ShowStatus(
                InfoBarSeverity.Error,
                global::CSIViewer.Core.Localization.Text("No se pudo borrar el historial"),
                global::CSIViewer.Core.Localization.Text("Cierra el servidor y vuelve a intentarlo. ") + ex.GetBaseException().Message);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadSettingsFromUi(out var validationError))
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Configuración no válida"), validationError);
            return;
        }

        if (SaveSettings(silent: false))
        {
            ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Configuración guardada"),
                global::CSIViewer.Core.Localization.Text("El modo consola usará estos mismos valores."));
        }
    }

    private bool SaveSettings(bool silent)
    {
        try
        {
            if (!ServerStartup.SetEnabled(_settings.StartWithWindows))
            {
                ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo cambiar el inicio con Windows"),
                    global::CSIViewer.Core.Localization.Text("Windows no permitió registrar la entrada de inicio automático."));
                return false;
            }

            ServerSettingsStore.Save(_settings);
            SaveHint.Text = global::CSIViewer.Core.Localization.Format("Guardado a las {0:HH:mm}",DateTime.Now);
            if (silent)
            {
                StatusBar.IsOpen = false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo guardar la configuración"),
                ex.GetBaseException().Message);
            return false;
        }
    }

    private void StartWithWindowsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        // Auto-start at sign-in is only useful if the server also starts when
        // the UI opens, so enable both together. The user can still turn the
        // second option off afterwards.
        if (StartWithWindowsSwitch.IsOn)
        {
            AutoStartSwitch.IsOn = true;
        }
    }

    private AppLogLevel SelectedLogLevel => _selectedLogLevel;

    private void LogLevelItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<AppLogLevel>(tag, ignoreCase: true, out var level))
        {
            return;
        }

        ApplyLogLevelSelection(level);
        LogLevelButton.Flyout?.Hide();
        if (!_loaded)
        {
            return;
        }

        _settings.LogLevel = level;
        if (_host.LoggingOptions is not null)
        {
            _host.LoggingOptions.MinimumLevel = level.ToMicrosoft();
        }
    }

    private void ApplyLogLevelSelection(AppLogLevel level)
    {
        _selectedLogLevel = level;
        LogLevelButton.Content = DescribeLogLevel(level);
        SetLogLevelItem(DebugLevelItem, DebugLevelBar, level == AppLogLevel.Debug);
        SetLogLevelItem(InformationLevelItem, InformationLevelBar, level == AppLogLevel.Information);
        SetLogLevelItem(WarningLevelItem, WarningLevelBar, level == AppLogLevel.Warning);
        SetLogLevelItem(ErrorLevelItem, ErrorLevelBar, level == AppLogLevel.Error);
        SetLogLevelItem(CriticalLevelItem, CriticalLevelBar, level == AppLogLevel.Critical);
    }

    private static void SetLogLevelItem(Button item, Border bar, bool selected)
    {
        bar.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(item, selected ? "Seleccionado" : "");
    }

    private void LogLevelFlyout_Opened(object? sender, object e)
    {
        var item = _selectedLogLevel switch
        {
            AppLogLevel.Debug => DebugLevelItem,
            AppLogLevel.Warning => WarningLevelItem,
            AppLogLevel.Error => ErrorLevelItem,
            AppLogLevel.Critical => CriticalLevelItem,
            _ => InformationLevelItem
        };
        item.Focus(FocusState.Programmatic);
    }

    private static string DescribeLogLevel(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => global::CSIViewer.Core.Localization.Text("Depuración (máximo detalle)"),
        AppLogLevel.Warning => global::CSIViewer.Core.Localization.Text("Advertencia"),
        AppLogLevel.Error => global::CSIViewer.Core.Localization.Text("Error"),
        AppLogLevel.Critical => global::CSIViewer.Core.Localization.Text("Crítico"),
        _ => global::CSIViewer.Core.Localization.Text("Información")
    };

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(ServerSettingsStore.DataDirectory, "logs");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus(
                InfoBarSeverity.Error,
                global::CSIViewer.Core.Localization.Text("No se pudo abrir la carpeta de logs"),
                ex.GetBaseException().Message);
        }
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CSIViewerServerDiagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            var sourceLogs = Path.Combine(ServerSettingsStore.DataDirectory, "logs");
            if (Directory.Exists(sourceLogs))
            {
                foreach (var file in Directory.EnumerateFiles(sourceLogs, global::CSIViewer.Core.Localization.Text("csiviewer-servidor-*.log*")))
                {
                    File.Copy(file, Path.Combine(logs, Path.GetFileName(file)), overwrite: true);
                }
            }

            var summary = new StringBuilder()
                .AppendLine("CSIViewer Servidor - diagnóstico")
                .AppendLine($"Generado: {DateTimeOffset.Now:O}")
                .AppendLine($"Versión: {typeof(MainWindow).Assembly.GetName().Version}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"Servidor en ejecución: {_host.IsRunning}")
                .AppendLine($"URL: {SensitiveDataRedactor.Redact(_host.ActiveUrl ?? _settings.ListenUrl)}")
                .AppendLine($"Base de datos: {_settings.DatabasePath}")
                .AppendLine($"LogLevel: {_settings.LogLevel}")
                .AppendLine()
                .AppendLine("No se incluyen contraseñas, tokens, códigos de inscripción ni claves privadas.")
                .ToString();
            File.WriteAllText(Path.Combine(root, "diagnostics.txt"), summary, Encoding.UTF8);

            var zip = Path.Combine(
                Path.GetTempPath(),
                $"CSIViewerServidor-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(root, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
            ShowStatus(
                InfoBarSeverity.Success,
                global::CSIViewer.Core.Localization.Text("Diagnóstico exportado"),
                zip);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus(
                InfoBarSeverity.Error,
                global::CSIViewer.Core.Localization.Text("No se pudo exportar el diagnóstico"),
                ex.GetBaseException().Message);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Temporary diagnostics directory is best effort.
            }
        }
    }

    private bool ReadSettingsFromUi()
    {
        return ReadSettingsFromUi(out _);
    }

    private bool ReadSettingsFromUi(out string error)
    {
        if (double.IsNaN(PortBox.Value))
        {
            error = global::CSIViewer.Core.Localization.Text("Escribe un puerto válido.");
            return false;
        }

        var databasePath = DatabasePathBox.Text.Trim();
        if (databasePath.Length == 0)
        {
            error = global::CSIViewer.Core.Localization.Text("Indica una ruta para la base de datos.");
            return false;
        }

        if (!Path.IsPathRooted(databasePath))
        {
            error = global::CSIViewer.Core.Localization.Text("La ruta de la base de datos debe ser absoluta.");
            return false;
        }

        _settings.Port = (int)PortBox.Value;
        _settings.ListenOnAllInterfaces = AllInterfacesSwitch.IsOn;
        _settings.EnrollmentCode = EnrollmentCodeBox.Text;
        _settings.ServerName = ServerNameBox.Text;
        _settings.DatabasePath = databasePath;
        _settings.EnableUpnp = UpnpSwitch.IsOn;
        _settings.UpnpExternalPort = double.IsNaN(UpnpExternalPortBox.Value)
            ? 0
            : (int)UpnpExternalPortBox.Value;
        _settings.UseHttps = HttpsSwitch.IsOn;
        _settings.CertificateThumbprint = CertificateThumbprintBox.Text;
        _settings.EnableStunServer = StunSwitch.IsOn;
        _settings.StunPort = double.IsNaN(StunPortBox.Value) ? 3478 : (int)StunPortBox.Value;
        _settings.MaxSessionsPerHost = double.IsNaN(MaxSessionsPerHostBox.Value)
            ? 10
            : (int)MaxSessionsPerHostBox.Value;
        _settings.AutoStartWithUi = AutoStartSwitch.IsOn;
        _settings.StartWithWindows = StartWithWindowsSwitch.IsOn;
        _settings.LogLevel = SelectedLogLevel;
        _settings.Normalize();
        error = "";
        return true;
    }

    private void UpdateStatus()
    {
        var localRunning = _host.IsRunning;
        var serviceRunning = _serviceStatus?.Running == true;
        var running = localRunning || serviceRunning;
        RunningDot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StoppedDot.Visibility = running ? Visibility.Collapsed : Visibility.Visible;

        if (serviceRunning && !localRunning)
        {
            StatusTitle.Text = global::CSIViewer.Core.Localization.Text("Servicio en ejecución");
            StatusDetail.Text = global::CSIViewer.Core.Localization.Text("El servidor se está ejecutando como servicio de Windows. ") +
                                global::CSIViewer.Core.Localization.Text("Usa «Parar servicio» en la tarjeta Servicio de Windows para detenerlo.");
            StatusUrl.Text = _settings.ListenOnAllInterfaces
                ? BuildAddressText()
                : global::CSIViewer.Core.Localization.Format("Dirección configurada: {0}",_settings.ListenUrl);
        }
        else if (localRunning)
        {
            StatusTitle.Text = global::CSIViewer.Core.Localization.Text("Servidor en ejecución");
            StatusDetail.Text = _settings.ListenOnAllInterfaces
                ? global::CSIViewer.Core.Localization.Text("Aceptando conexiones desde la red local o Internet. Direcciones disponibles:")
                : global::CSIViewer.Core.Localization.Text("Aceptando conexiones solo desde este equipo.");
            StatusUrl.Text = BuildAddressText();
        }
        else
        {
            StatusTitle.Text = global::CSIViewer.Core.Localization.Text("Servidor detenido");
            StatusDetail.Text = global::CSIViewer.Core.Localization.Text("Pulsa «Iniciar servidor» para aceptar conexiones.");
            StatusUrl.Text = global::CSIViewer.Core.Localization.Format("Dirección configurada: {0}",_settings.ListenUrl);
        }

        BusyRing.IsActive = _busy;
        BusyRing.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = !_busy && !running;
        StopButton.IsEnabled = !_busy && localRunning;
        var configurationEnabled = !_busy && !running;
        ConfigContent.IsHitTestVisible = configurationEnabled;
        ConfigContent.Opacity = configurationEnabled ? 1 : 0.6;
        _tray?.SetRunning(running);
    }

    private string BuildAddressText()
    {
        var addresses = new List<string>();
        if (!_settings.ListenOnAllInterfaces)
        {
            addresses.Add(_settings.ClientUrl);
        }
        else
        {
            try
            {
                addresses.AddRange(Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
                    .Select(x => $"{_settings.Scheme}://{x}:{_settings.Port}"));
            }
            catch
            {
                // The listening address is still shown below.
            }

            addresses.Add(_settings.ListenUrl);
        }

        return global::CSIViewer.Core.Localization.Text("Dirección: ") + string.Join("  ·  ", addresses.Distinct());
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private async void GenerateCertificate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _host.IsRunning)
        {
            return;
        }

        _busy = true;
        UpdateStatus();
        try
        {
            var thumbprint = await Task.Run(CreateSelfSignedCertificate);
            CertificateThumbprintBox.Text = thumbprint;
            HttpsSwitch.IsOn = true;
            ShowStatus(InfoBarSeverity.Success, "Certificado creado",
                "Huella " + thumbprint + ". Instalado en CurrentUser\\My. " +
                "Para el servicio, usa scripts\\new-server-certificate.ps1 -Machine.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo crear el certificado"),
                ex.GetBaseException().Message);
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private static string CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            global::CSIViewer.Core.Localization.Text("CN=CSIViewer Servidor"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                false));

        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddDnsName("localhost");
        alternativeNames.AddDnsName(Environment.MachineName);
        alternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(alternativeNames.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));
        var pfx = certificate.Export(X509ContentType.Pfx);
        var persisted = X509CertificateLoader.LoadPkcs12(
            pfx,
            null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(persisted);
        return persisted.Thumbprint;
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        SaveWindowPlacementNow();
        _tray?.Dispose();
        _tray = null;
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private async void BrowseDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = "server.db"
            };
            picker.FileTypeChoices.Add(global::CSIViewer.Core.Localization.Text("Base de datos SQLite"), new List<string> { ".db" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                DatabasePathBox.Text = file.Path;
            }
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo elegir la base de datos"),
                ex.GetBaseException().Message);
        }
    }
}
