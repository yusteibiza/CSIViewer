using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Services;
using CSIViewer.Host.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CSIViewer.Host.Views;

public sealed partial class HostSettingsPage : Page
{
    private void Cards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 560;
        var cards = new[] { AppearanceCard, ServerCard, QualityCard, UnattendedCard };
        for (var i = 0; i < cards.Length; i++)
        {
            Grid.SetRow(cards[i], narrow ? i : i / 2);
            Grid.SetColumn(cards[i], narrow ? 0 : i % 2);
            Grid.SetColumnSpan(cards[i], narrow ? 2 : 1);
        }
    }

    private static readonly int[] FpsPresets = [12, 15, 24, 30, 50, 60, 90, 120];

    private readonly AppState _state;
    private readonly ISettingsStore _settings;
    private readonly RemoteClient _client;
    private readonly DiagnosticsService _diagnostics;
    private readonly IHistoryRepository _history;
    private readonly UnattendedServiceManager _unattended = new();
    private bool _loading;
    private bool _unattendedBusy;
    private bool _profileChanging;

    public HostSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        InitializeAccentOptions();
        _settings = App.Services.GetRequiredService<ISettingsStore>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _diagnostics = App.Services.GetRequiredService<DiagnosticsService>();
        _history = App.Services.GetRequiredService<IHistoryRepository>();
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
        Loaded += async (_, _) =>
        {
            LoadSettings();
            await RefreshUnattendedStatusAsync();
        };
    }

    private void LoadSettings()
    {
        _loading = true;
        var settings = _state.Settings;
        SynchronizeAccentSelection();
        ThemeCombo.SelectedIndex = settings.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };
        ServerUrlBox.Text = settings.ServerUrl;
        AllowInsecureSwitch.IsOn = settings.AllowInsecureServer;
        DownloadFolderBox.Text = settings.DownloadFolder;
        ConsentModeCombo.SelectedIndex = settings.HostConsentMode switch
        {
            HostConsentMode.AcceptAutomatically => 1,
            HostConsentMode.RejectAutomatically => 2,
            _ => 0
        };
        ServerStatusText.Text = _state.ServerStatus;
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
        QualityValueText.Text = settings.ImageQuality.ToString();
        MaxWidthValueText.Text = settings.MaxWidth.ToString();
        H264Switch.IsOn = settings.PreferH264;
        ModernCaptureSwitch.IsOn = settings.PreferModernCapture;
        SelectProfileFromSettings(settings.ImageQuality, settings.Fps, settings.MaxWidth);
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

    private async void QualityProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
        await SaveQualityAsync();
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

    private async void SaveServer_Click(object sender, RoutedEventArgs e)
    {
        await SaveServerAsync(reconnect: true);
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveServerAsync(reconnect: false))
        {
            return;
        }

        ServerStatusText.Text = global::CSIViewer.Core.Localization.Text("Comprobando servidor…");
        ServerStatusText.Text = await _diagnostics.TestServerAsync();
    }

    private async Task<bool> SaveServerAsync(bool reconnect)
    {
        var url = ServerUrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowStatus(InfoBarSeverity.Warning, global::CSIViewer.Core.Localization.Text("URL no válida"),
                global::CSIViewer.Core.Localization.Text("Escribe una dirección http:// o https:// completa, por ejemplo http://localhost:5080."));
            return false;
        }

        var previous = _state.Settings.ServerUrl;
        _state.Settings.ServerUrl = url;
        _state.Settings.AllowInsecureServer = AllowInsecureSwitch.IsOn;
        _state.Settings.Normalize();

        try
        {
            await _settings.SaveAsync(_state.Settings);
            ServerStatusText.Text = _state.Settings.ServerUrl;
            if (reconnect &&
                !string.Equals(previous, _state.Settings.ServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                await _client.DisconnectFromServerAsync();
                await _client.ConnectToServerAsync();
            }

            ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Servidor"),
                reconnect ? global::CSIViewer.Core.Localization.Text("Servidor guardado. Reconectando…") : global::CSIViewer.Core.Localization.Text("Servidor guardado."));
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Servidor"),
                global::CSIViewer.Core.Localization.Text("No se pudo guardar el servidor: ") + ex.GetBaseException().Message);
            return false;
        }
    }

    private async void BrowseDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(MainWindow.Instance));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            DownloadFolderBox.Text = folder.Path;
        }
    }

    private async void SaveDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        _state.Settings.DownloadFolder = DownloadFolderBox.Text.Trim();
        _state.Settings.Normalize();
        try
        {
            await _settings.SaveAsync(_state.Settings);
            DownloadFolderBox.Text = _state.Settings.DownloadFolder;
            ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Archivos"), global::CSIViewer.Core.Localization.Text("Carpeta de descargas guardada."));
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Archivos"),
                global::CSIViewer.Core.Localization.Text("No se pudo guardar la carpeta: ") + ex.GetBaseException().Message);
        }
    }

    private async void ConsentMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _state.Settings.HostConsentMode = ConsentModeCombo.SelectedIndex switch
        {
            1 => HostConsentMode.AcceptAutomatically,
            2 => HostConsentMode.RejectAutomatically,
            _ => HostConsentMode.Ask
        };

        try
        {
            await _settings.SaveAsync(_state.Settings);
            ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Solicitudes de conexión"),
                _state.Settings.HostConsentMode switch
                {
                    HostConsentMode.AcceptAutomatically => global::CSIViewer.Core.Localization.Text("Las próximas solicitudes se aceptarán automáticamente con los permisos solicitados."),
                    HostConsentMode.RejectAutomatically => global::CSIViewer.Core.Localization.Text("Las próximas solicitudes se rechazarán automáticamente."),
                    _ => global::CSIViewer.Core.Localization.Text("Se preguntará antes de aceptar cada conexión.")
                });
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Solicitudes de conexión"),
                global::CSIViewer.Core.Localization.Text("No se pudo guardar la política: ") + ex.GetBaseException().Message);
        }
    }

    private async void SaveQuality_Click(object sender, RoutedEventArgs e)
    {
        await SaveQualityAsync();
    }

    private async Task SaveQualityAsync()
    {
        var settings = _state.Settings;
        settings.ImageQuality = (int)QualitySlider.Value;
        settings.Fps = FpsCombo.SelectedItem is int fps ? fps : settings.Fps;
        settings.MaxWidth = (int)MaxWidthSlider.Value;
        settings.PreferH264 = H264Switch.IsOn;
        settings.PreferModernCapture = ModernCaptureSwitch.IsOn;
        settings.Normalize();

        try
        {
            await _settings.SaveAsync(settings);
            var applied = _client.IsHostCapturing;
            await _client.RestartHostCaptureAsync();
            ShowStatus(InfoBarSeverity.Success, global::CSIViewer.Core.Localization.Text("Calidad de imagen"),
                applied
                    ? global::CSIViewer.Core.Localization.Text("Calidad aplicada a la sesión actual.")
                    : global::CSIViewer.Core.Localization.Text("Calidad guardada. Se aplicará al iniciar la próxima sesión."));
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("Calidad de imagen"),
                global::CSIViewer.Core.Localization.Text("No se pudo guardar la calidad: ") + ex.GetBaseException().Message);
        }
    }

    private async void InstallUnattended_Click(object sender, RoutedEventArgs e)
    {
        if (_unattendedBusy)
        {
            return;
        }

        SetUnattendedBusy(true);
        try
        {
            var result = await _unattended.InstallAsync();
            var success = result.StartsWith("Operación completada", StringComparison.Ordinal);
            ShowStatus(success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                global::CSIViewer.Core.Localization.Text("Modo desatendido"), result);
        }
        finally
        {
            SetUnattendedBusy(false);
            await RefreshUnattendedStatusAsync();
        }
    }

    private async void UninstallUnattended_Click(object sender, RoutedEventArgs e)
    {
        if (_unattendedBusy)
        {
            return;
        }

        SetUnattendedBusy(true);
        try
        {
            var result = await _unattended.UninstallAsync();
            ShowStatus(result.StartsWith("Operación completada", StringComparison.Ordinal)
                    ? InfoBarSeverity.Success
                    : InfoBarSeverity.Error,
                global::CSIViewer.Core.Localization.Text("Modo desatendido"), result);
        }
        finally
        {
            SetUnattendedBusy(false);
            await RefreshUnattendedStatusAsync();
        }
    }

    private async void RefreshUnattended_Click(object sender, RoutedEventArgs e)
    {
        if (!_unattendedBusy)
        {
            await RefreshUnattendedStatusAsync();
        }
    }

    private async Task RefreshUnattendedStatusAsync()
    {
        try
        {
            UnattendedStatusText.Text = global::CSIViewer.Core.Localization.Text("Comprobando.");
            var status = await _unattended.GetStatusAsync();
            UnattendedStatusText.Text = status;
            InstallUnattendedButton.IsEnabled = !_unattendedBusy && status == "No instalado";
            UninstallUnattendedButton.IsEnabled = !_unattendedBusy && status != "No instalado";
        }
        catch (Exception ex)
        {
            UnattendedStatusText.Text = global::CSIViewer.Core.Localization.Text("No se pudo consultar");
            ShowStatus(InfoBarSeverity.Warning, global::CSIViewer.Core.Localization.Text("Modo desatendido"),
                global::CSIViewer.Core.Localization.Text("No se pudo consultar el servicio: ") + ex.GetBaseException().Message);
        }
    }

    private void SetUnattendedBusy(bool busy)
    {
        _unattendedBusy = busy;
        UnattendedBusyRing.IsActive = busy;
        UnattendedBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        InstallUnattendedButton.IsEnabled = !busy;
        UninstallUnattendedButton.IsEnabled = !busy;
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = XamlRoot,
            Title = global::CSIViewer.Core.Localization.Text("Borrar todo el historial"),
            Content = global::CSIViewer.Core.Localization.Text("Se eliminarán permanentemente todas las conexiones guardadas en este equipo. ") +
                      global::CSIViewer.Core.Localization.Text("No se borran los dispositivos, los contactos ni las sesiones activas."),
            PrimaryButtonText = "Borrar todo",
            CloseButtonText = global::CSIViewer.Core.Localization.Text("Cancelar"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _history.DeleteAllAsync();
            _state.History.Clear();
            ShowStatus(InfoBarSeverity.Success, "Historial borrado",
                global::CSIViewer.Core.Localization.Text("Todas las conexiones guardadas se han eliminado."));
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, global::CSIViewer.Core.Localization.Text("No se pudo borrar el historial"),
                ex.GetBaseException().Message);
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
