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
        CSIViewer.Presentation.AccentAppearance.Changed += (_, _) => DispatcherQueue.TryEnqueue(SynchronizeAccentSelection);
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

    private void LoadSettings()
    {
        _loading = true;
        var settings = _state.Settings;
        AccentColorCombo.ItemsSource = AccentPalette.Choices;
        AccentColorCombo.SelectedItem = AccentPalette.Choices.First(x => x.Value == AccentPalette.Normalize(settings.AccentColor));
        ThemeCombo.SelectedIndex = settings.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };
        LanguageCombo.SelectedIndex = 0;
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
        StartWithWindowsSwitch.IsOn = _startup.IsEnabled;
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

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var theme = ThemeCombo.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System
        };
        ThemeService.Apply(theme);
        _state.Settings.Theme = theme;
        await _settings.SaveAsync(_state.Settings);
    }

    private void SynchronizeAccentSelection()
    {
        var loading = _loading;
        _loading = true;
        AccentColorCombo.SelectedItem = AccentPalette.Choices.First(x => x.Value == AccentPalette.Normalize(_state.Settings.AccentColor));
        _loading = loading;
    }

    private async void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentColorCombo.SelectedItem is not AccentChoice choice) return;
        _state.Settings.AccentColor = choice.Value;
        CSIViewer.Presentation.AccentAppearance.Apply(choice.Value, MainWindow.Instance?.Content as FrameworkElement);
        try { await _settings.SaveAsync(_state.Settings); }
        catch (Exception)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Warning;
            SettingsStatusBar.Message = "El color se ha aplicado, pero no se pudo guardar.";
            SettingsStatusBar.IsOpen = true;
        }
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

    private async void SaveAndReconnect_Click(object sender, RoutedEventArgs e)
    {
        await SaveCoreAsync(reconnect: true);
    }

    private async Task SaveCoreAsync(bool reconnect)
    {
        var settings = _state.Settings;
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
        _loggingOptions.MinimumLevel = settings.LogLevel.ToMicrosoft();
        var startupRegistered = _startup.SetEnabled(StartWithWindowsSwitch.IsOn);
        settings.StartWithWindows = _startup.IsEnabled;
        StartWithWindowsSwitch.IsOn = settings.StartWithWindows;
        settings.NotificationsEnabled = NotificationsSwitch.IsOn;
        settings.AskBeforeReceivingFiles = AskFilesSwitch.IsOn;
        settings.StoreConnectionHistory = HistorySwitch.IsOn;
        settings.Normalize();

        try
        {
            await _identity.SetDeviceNameAsync(settings.DeviceName);
            await _settings.SaveAsync(settings);
            ThemeService.Apply(settings.Theme);
            _state.Post(() =>
            {
                _state.DeviceName = settings.DeviceName;
                _state.Settings = settings;
                _state.StatusMessage = "Configuración guardada.";
            });

            if (reconnect)
            {
                await _client.DisconnectFromServerAsync();
                await _client.ConnectToServerAsync();
            }

            SettingsStatusBar.Severity = startupRegistered
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            SettingsStatusBar.Message = startupRegistered
                ? "Configuración guardada."
                : "Configuración guardada, pero Windows no permitió cambiar el inicio automático.";
            SettingsStatusBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Message = "No se pudo guardar la configuración: " + ex.Message;
            SettingsStatusBar.IsOpen = true;
        }
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = "Comprobando servidor…";
        DiagnosticsText.Text = await _diagnostics.TestServerAsync();
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
            await _client.RevokeAllSessionsAsync("Acceso revocado por el usuario.");
            SettingsStatusBar.Severity = InfoBarSeverity.Success;
            SettingsStatusBar.Message = "Todas las sesiones cerradas y contraseña temporal regenerada.";
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Message = "No se pudo revocar el acceso: " + ex.GetBaseException().Message;
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

        _loggingOptions.MinimumLevel = level.ToMicrosoft();
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
        AppLogLevel.Debug => "Depuración (máximo detalle)",
        AppLogLevel.Warning => "Advertencia",
        AppLogLevel.Error => "Error",
        AppLogLevel.Critical => "Crítico",
        _ => "Información"
    };

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = "Creando paquete de diagnóstico…";
        try
        {
            var path = await _diagnostics.ExportAsync();
            DiagnosticsText.Text = "Diagnóstico exportado:\n" + path;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DiagnosticsText.Text = "No se pudo exportar el diagnóstico: " + ex.GetBaseException().Message;
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
            SettingsStatusBar.Message = "No se pudo abrir la carpeta de logs.";
            SettingsStatusBar.IsOpen = true;
        }
    }
}
