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
        if (sender is Grid grid)
        {
            var rows = narrow ? 4 : 2;
            while (grid.RowDefinitions.Count < rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            while (grid.RowDefinitions.Count > rows) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        }
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
    private bool _unattendedChecking;
    private int _unattendedStatusRequest;
    private HostStartupStatus _unattendedStatus = new(HostStartupState.Unknown);

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

    private void LoadSettings(AppSettings? source = null)
    {
        _loading = true;
        var settings = source ?? _state.Settings;
        SynchronizeAccentSelection(settings);
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

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        ServerStatusText.Text = global::CSIViewer.Core.Localization.Text("Comprobando servidor…");
        ServerStatusText.Text = await _diagnostics.TestServerAsync(ServerUrlBox.Text.Trim(), _state.Settings.AllowInsecureServer);
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
            var success = _unattended.LastOperationSucceeded;
            if (_unattended.LastConfirmedStatus is { } verified) _unattendedStatus = verified;
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
            if (_unattended.LastConfirmedStatus is { } verified) _unattendedStatus = verified;
            ShowStatus(_unattended.LastOperationSucceeded
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
            await RefreshUnattendedStatusAsync(allowElevation: true);
        }
    }

    private async Task RefreshUnattendedStatusAsync(bool allowElevation = false)
    {
        var request = ++_unattendedStatusRequest;
        _unattendedChecking = true;
        ApplyUnattendedButtons();
        try
        {
            UnattendedStatusText.Text = global::CSIViewer.Core.Localization.Text("Comprobando.");
            var status = await _unattended.GetStatusDetailsAsync(allowElevation);
            if (request != _unattendedStatusRequest) return;
            _unattendedStatus = status;
            UnattendedStatusText.Text = status.Text;
        }
        catch (Exception ex)
        {
            if (request != _unattendedStatusRequest) return;
            _unattendedStatus = new(HostStartupState.Unknown);
            UnattendedStatusText.Text = global::CSIViewer.Core.Localization.Text("No se pudo consultar");
            ShowStatus(InfoBarSeverity.Warning, global::CSIViewer.Core.Localization.Text("Modo desatendido"),
                global::CSIViewer.Core.Localization.Text("No se pudo consultar el servicio: ") + ex.GetBaseException().Message);
        }
        finally
        {
            if (request == _unattendedStatusRequest) { _unattendedChecking = false; ApplyUnattendedButtons(); }
        }
    }

    private void SetUnattendedBusy(bool busy)
    {
        _unattendedBusy = busy;
        if (busy) { ++_unattendedStatusRequest; _unattendedChecking = false; }
        ApplyUnattendedButtons();
    }

    private void ApplyUnattendedButtons()
    {
        var busy = _unattendedBusy || _unattendedChecking;
        UnattendedBusyRing.IsActive = busy;
        UnattendedBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        InstallUnattendedButton.IsEnabled = !busy && _unattendedStatus.CanActivate;
        UninstallUnattendedButton.IsEnabled = !busy && _unattendedStatus.CanDeactivate;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
