using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Infrastructure;
using CSIViewer.Services;

namespace CSIViewer.Views;

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
    private readonly StartupService _startup;
    private readonly UnattendedServiceManager _unattended = new();
    private bool _loading;
    private bool _unattendedBusy;

    public HostSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        InitializeAccentOptions();
        _settings = App.Services.GetRequiredService<ISettingsStore>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _diagnostics = App.Services.GetRequiredService<DiagnosticsService>();
        _startup = App.Services.GetRequiredService<StartupService>();
        QualitySlider.ValueChanged += (_, _) =>
            QualityValueText.Text = ((int)QualitySlider.Value).ToString();
        MaxWidthSlider.ValueChanged += (_, _) =>
            MaxWidthValueText.Text = ((int)MaxWidthSlider.Value).ToString();
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
        _loading = false;
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

    private async void SaveQuality_Click(object sender, RoutedEventArgs e)
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
            if (success && _startup.IsEnabled)
            {
                // The service starts the host in the user session. Keeping the
                // per-user Run entry would start a second instance first.
                _startup.SetEnabled(false);
                _state.Settings.StartWithWindows = false;
                await _settings.SaveAsync(_state.Settings);
            }

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

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
