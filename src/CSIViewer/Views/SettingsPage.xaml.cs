using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CSIViewer.Views;

public sealed partial class SettingsPage : Page
{
    private void ConfigurationHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 850;
        Grid.SetRow(ConfigurationActions, narrow ? 1 : 0);
        Grid.SetColumn(ConfigurationActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(ConfigurationActions, narrow ? 2 : 1);
    }

    private static readonly int[] FpsPresets = [12, 15, 24, 30, 50, 60, 90, 120];

    private readonly AppState _state;
    private readonly ISettingsStore _settings;
    private readonly IdentityService _identity;
    private readonly RemoteClient _client;
    private readonly StartupService _startup;
    private readonly DiagnosticsService _diagnostics;
    private readonly LoggingOptions _loggingOptions;
    private bool _loading;
    private bool _profileChanging;
    private AppLogLevel _selectedLogLevel = AppLogLevel.Information;

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _settings = App.Services.GetRequiredService<ISettingsStore>();
        _identity = App.Services.GetRequiredService<IdentityService>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _startup = App.Services.GetRequiredService<StartupService>();
        _diagnostics = App.Services.GetRequiredService<DiagnosticsService>();
        _loggingOptions = App.Services.GetRequiredService<LoggingOptions>();
        Loaded += (_, _) => LoadSettings();
        QualitySlider.ValueChanged += (_, _) =>
        {
            QualityValueText.Text = ((int)QualitySlider.Value).ToString();
            MarkCustomProfile();
        };
        MaxWidthSlider.ValueChanged += (_, _) =>
        {
            MaxWidthValueText.Text = ((int)MaxWidthSlider.Value).ToString();
            MarkCustomProfile();
        };
        FpsCombo.SelectionChanged += (_, _) => MarkCustomProfile();
    }

    private void LoadSettings(AppSettings? source = null)
    {
        _loading = true;
        var settings = source ?? _state.Settings;
        AccentColorCombo.ItemsSource = AccentPalette.Choices;
        AccentColorCombo.SelectedItem = AccentPalette.Choices.First(x => x.Value == AccentPalette.Normalize(settings.AccentColor));
        ThemeCombo.SelectedIndex = settings.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };
        LanguageCombo.ItemsSource = Localization.Languages;
        LanguageCombo.SelectedItem = Localization.Languages.First(x => x.Code == Localization.Normalize(settings.Language));
        DeviceNameBox.Text = settings.DeviceName;
        ServerUrlBox.Text = settings.ServerUrl;
        EnrollmentCodeBox.Text = settings.EnrollmentCode;
        QualitySlider.Value = settings.ImageQuality;
        var fpsOptions = FpsPresets.ToList();
        if (settings.Fps > 0 && !fpsOptions.Contains(settings.Fps))
        {
            fpsOptions.Add(settings.Fps);
            fpsOptions.Sort();
        }

        FpsCombo.ItemsSource = fpsOptions;
        FpsCombo.SelectedItem = settings.Fps;
        MaxWidthSlider.Value = settings.MaxWidth;
        DownloadFolderBox.Text = settings.DownloadFolder;
        FileConflictCombo.SelectedIndex = settings.FileConflictPolicy switch
        {
            FileConflictPolicy.Overwrite => 1,
            FileConflictPolicy.Skip => 2,
            _ => 0
        };
        AutoResumeTransfersSwitch.IsOn = settings.AutoResumeTransfers;
        UploadLimitBox.Value = settings.UploadLimitKbps;
        AllowIncomingSwitch.IsOn = settings.AllowIncomingConnections;
        MaxSessionsBox.Value = settings.MaxConcurrentSessions;
        TemporaryPasswordLifetimeBox.Value = settings.TemporaryPasswordLifetimeMinutes;
        AllowInsecureSwitch.IsOn = settings.AllowInsecureServer;
        HeartbeatIntervalBox.Value = settings.HeartbeatIntervalSeconds;
        HeartbeatTimeoutBox.Value = settings.HeartbeatTimeoutSeconds;
        ConnectTimeoutBox.Value = settings.ConnectTimeoutSeconds;
        MessageTimeoutBox.Value = settings.MessageTimeoutSeconds;
        ReconnectRetriesBox.Value = settings.ReconnectMaxRetries;
        ReconnectInitialDelayBox.Value = settings.ReconnectInitialDelaySeconds;
        ReconnectMaxDelayBox.Value = settings.ReconnectMaxDelaySeconds;
        ReconnectJitterBox.Value = settings.ReconnectJitterPercent;
        StartWithWindowsSwitch.IsOn = source is null ? _startup.IsEnabled : settings.StartWithWindows;
        NotificationsSwitch.IsOn = settings.NotificationsEnabled;
        AskFilesSwitch.IsOn = settings.AskBeforeReceivingFiles;
        HistorySwitch.IsOn = settings.StoreConnectionHistory;
        QualityValueText.Text = settings.ImageQuality.ToString();
        MaxWidthValueText.Text = settings.MaxWidth.ToString();
        ModernCaptureSwitch.IsOn = settings.PreferModernCapture;
        H264Switch.IsOn = settings.PreferH264;
        DirectLanSwitch.IsOn = settings.PreferDirectLan;
        SelectProfileFromSettings(settings.ImageQuality, settings.Fps, settings.MaxWidth);
        ApplyLogLevelSelection(settings.LogLevel);
        ServerHintText.Text = _state.ServerStatus;
        _loading = false;
    }

    private void SelectProfileFromSettings(int quality, int fps, int maxWidth)
    {
        _profileChanging = true;
        QualityProfileCombo.SelectedIndex = (quality, fps, maxWidth) switch
        {
            (>= 88, >= 30, >= 2400) => 0,
            (>= 70, >= 24, >= 1800) => 1,
            (<= 60, <= 18, <= 1400) => 2,
            _ => 3
        };
        _profileChanging = false;
    }

    private void MarkCustomProfile()
    {
        if (_loading || _profileChanging)
        {
            return;
        }

        _profileChanging = true;
        QualityProfileCombo.SelectedIndex = 3;
        _profileChanging = false;
    }

    private void QualityProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _profileChanging ||
            QualityProfileCombo.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            tag == "Custom")
        {
            return;
        }

        var (quality, fps, width) = tag switch
        {
            "Sharp" => (92, 30, 2560),
            "Balanced" => (78, 24, 1920),
            "Speed" => (55, 15, 1280),
            _ => (0, 0, 0)
        };
        if (quality == 0)
        {
            return;
        }

        _profileChanging = true;
        QualitySlider.Value = quality;
        FpsCombo.SelectedItem = fps;
        MaxWidthSlider.Value = width;
        _profileChanging = false;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private bool _savingConfiguration;
    private void RestoreSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_savingConfiguration) { LoadSettings(); SettingsStatusBar.IsOpen = false; }
    }

    private void DefaultSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_savingConfiguration) { LoadSettings(new AppSettings()); SettingsStatusBar.IsOpen = false; }
    }

    private async void BrowseDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            DownloadFolderBox.Text = folder.Path;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveCoreAsync(reconnect: false);
    }

    private async Task SaveCoreAsync(bool reconnect)
    {
        if (_savingConfiguration) return;
        var settings = _state.Settings.Clone();
        settings.Theme = ThemeCombo.SelectedIndex switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.System };
        settings.AccentColor = (AccentColorCombo.SelectedItem as AccentChoice)?.Value ?? settings.AccentColor;
        settings.Language = (LanguageCombo.SelectedItem as LanguageChoice)?.Code ?? settings.Language;
        settings.DeviceName = DeviceNameBox.Text.Trim();
        settings.ServerUrl = ServerUrlBox.Text.Trim();
        settings.EnrollmentCode = EnrollmentCodeBox.Text.Trim();
        settings.ImageQuality = (int)QualitySlider.Value;
        settings.Fps = FpsCombo.SelectedItem is int fps ? fps : settings.Fps;
        settings.MaxWidth = (int)MaxWidthSlider.Value;
        settings.PreferModernCapture = ModernCaptureSwitch.IsOn;
        settings.PreferH264 = H264Switch.IsOn;
        settings.PreferDirectLan = DirectLanSwitch.IsOn;
        settings.DownloadFolder = DownloadFolderBox.Text.Trim();
        settings.FileConflictPolicy = FileConflictCombo.SelectedIndex switch
        {
            1 => FileConflictPolicy.Overwrite,
            2 => FileConflictPolicy.Skip,
            _ => FileConflictPolicy.Rename
        };
        settings.AutoResumeTransfers = AutoResumeTransfersSwitch.IsOn;
        settings.UploadLimitKbps = (int)UploadLimitBox.Value;
        settings.AllowIncomingConnections = AllowIncomingSwitch.IsOn;
        settings.MaxConcurrentSessions = double.IsNaN(MaxSessionsBox.Value)
            ? 10
            : (int)MaxSessionsBox.Value;
        settings.TemporaryPasswordLifetimeMinutes = double.IsNaN(TemporaryPasswordLifetimeBox.Value)
            ? 60
            : (int)TemporaryPasswordLifetimeBox.Value;
        settings.AllowInsecureServer = AllowInsecureSwitch.IsOn;
        settings.HeartbeatIntervalSeconds = double.IsNaN(HeartbeatIntervalBox.Value)
            ? 5
            : (int)HeartbeatIntervalBox.Value;
        settings.HeartbeatTimeoutSeconds = double.IsNaN(HeartbeatTimeoutBox.Value)
            ? 20
            : (int)HeartbeatTimeoutBox.Value;
        settings.ConnectTimeoutSeconds = double.IsNaN(ConnectTimeoutBox.Value)
            ? 10
            : (int)ConnectTimeoutBox.Value;
        settings.MessageTimeoutSeconds = double.IsNaN(MessageTimeoutBox.Value)
            ? 60
            : (int)MessageTimeoutBox.Value;
        settings.ReconnectMaxRetries = double.IsNaN(ReconnectRetriesBox.Value)
            ? 10
            : (int)ReconnectRetriesBox.Value;
        settings.ReconnectInitialDelaySeconds = double.IsNaN(ReconnectInitialDelayBox.Value)
            ? 2
            : (int)ReconnectInitialDelayBox.Value;
        settings.ReconnectMaxDelaySeconds = double.IsNaN(ReconnectMaxDelayBox.Value)
            ? 30
            : (int)ReconnectMaxDelayBox.Value;
        settings.ReconnectJitterPercent = double.IsNaN(ReconnectJitterBox.Value)
            ? 20
            : (int)ReconnectJitterBox.Value;
        settings.StartWithWindows = StartWithWindowsSwitch.IsOn;
        settings.LogLevel = SelectedLogLevel;
        settings.NotificationsEnabled = NotificationsSwitch.IsOn;
        settings.AskBeforeReceivingFiles = AskFilesSwitch.IsOn;
        settings.StoreConnectionHistory = HistorySwitch.IsOn;
        settings.Normalize();
        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Warning;
            SettingsStatusBar.Message = Localization.Text("URL no válida");
            SettingsStatusBar.IsOpen = true;
            return;
        }
        reconnect |= settings.ServerUrl != _state.Settings.ServerUrl || settings.EnrollmentCode != _state.Settings.EnrollmentCode ||
            settings.AllowInsecureServer != _state.Settings.AllowInsecureServer;
        _savingConfiguration = true;
        foreach (var button in ConfigurationActions.Children.OfType<Button>()) button.IsEnabled = false;
        var previousStartup = _startup.IsEnabled;
        var persisted = false;

        try
        {
            var startupRegistered = _startup.SetEnabled(settings.StartWithWindows);
            settings.StartWithWindows = _startup.IsEnabled;
            await _settings.SaveAsync(settings);
            persisted = true;
            _state.Settings = settings;
            _state.DeviceName = settings.DeviceName;
            _loggingOptions.MinimumLevel = settings.LogLevel.ToMicrosoft();
            ThemeService.Apply(settings.Theme);
            CSIViewer.Presentation.AccentAppearance.Apply(settings.AccentColor, MainWindow.Instance?.Content as FrameworkElement);
            if (MainWindow.Instance?.Content is FrameworkElement root) CSIViewer.Presentation.Translate.Apply(root, settings.Language);
            LoadSettings();

            SettingsStatusBar.Severity = startupRegistered
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            SettingsStatusBar.Message = startupRegistered
                ? global::CSIViewer.Core.Localization.Text("Configuración guardada.")
                : global::CSIViewer.Core.Localization.Text("Configuración guardada, pero Windows no permitió cambiar el inicio automático.");
            SettingsStatusBar.IsOpen = true;
            try
            {
                await _identity.SetDeviceNameAsync(settings.DeviceName, persistSettings: false);
                await _client.RestartHostCaptureAsync();
                if (reconnect) { await _client.DisconnectFromServerAsync(); await _client.ConnectToServerAsync(); }
            }
            catch (Exception ex)
            {
                SettingsStatusBar.Severity = InfoBarSeverity.Warning;
                SettingsStatusBar.Message = Localization.Text("Configuración guardada.") + " " + ex.GetBaseException().Message;
            }
        }
        catch (Exception ex)
        {
            if (!persisted) _startup.SetEnabled(previousStartup);
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Message = global::CSIViewer.Core.Localization.Text("No se pudo guardar la configuración: ") + ex.Message;
            SettingsStatusBar.IsOpen = true;
        }
        finally { _savingConfiguration = false; foreach (var button in ConfigurationActions.Children.OfType<Button>()) button.IsEnabled = true; }
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = global::CSIViewer.Core.Localization.Text("Comprobando servidor…");
        DiagnosticsText.Text = await _diagnostics.TestServerAsync(ServerUrlBox.Text.Trim(), AllowInsecureSwitch.IsOn);
    }

    private async void TestStun_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = "Consultando STUN…";
        DiagnosticsText.Text = await _diagnostics.TestStunAsync();
    }

    private async void RevokeAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _client.RevokeAllSessionsAsync(global::CSIViewer.Core.Localization.Text("Acceso revocado por el usuario."));
            SettingsStatusBar.Severity = InfoBarSeverity.Success;
            SettingsStatusBar.Message = global::CSIViewer.Core.Localization.Text("Todas las sesiones cerradas y contraseña temporal regenerada.");
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Message = global::CSIViewer.Core.Localization.Text("No se pudo revocar el acceso: ") + ex.GetBaseException().Message;
        }

        SettingsStatusBar.IsOpen = true;
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
        if (_loading)
        {
            return;
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

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = global::CSIViewer.Core.Localization.Text("Creando paquete de diagnóstico…");
        try
        {
            var path = await _diagnostics.ExportAsync();
            DiagnosticsText.Text = global::CSIViewer.Core.Localization.Text("Diagnóstico exportado:\n") + path;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticsText.Text = global::CSIViewer.Core.Localization.Text("No se pudo exportar el diagnóstico: ") + ex.GetBaseException().Message;
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppPaths.LogsDirectory;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Message = global::CSIViewer.Core.Localization.Text("No se pudo abrir la carpeta de logs.");
            SettingsStatusBar.IsOpen = true;
        }
    }
}
