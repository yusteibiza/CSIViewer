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
    private readonly IHistoryRepository _history;
    private readonly UnattendedServiceManager _unattended = new();
    private bool _loading;
    private bool _unattendedBusy;
    private bool _unattendedChecking;
    private int _unattendedStatusRequest;
    private HostStartupStatus _unattendedStatus = new(HostStartupState.Unknown);
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

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        ServerStatusText.Text = global::CSIViewer.Core.Localization.Text("Comprobando servidor…");
        ServerStatusText.Text = await _diagnostics.TestServerAsync(ServerUrlBox.Text.Trim(), AllowInsecureSwitch.IsOn);
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

    private void ConsentMode_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void WindowsLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CSIViewer Host");
            var script = Path.Combine(root, "configure-windows-login.ps1");
            if (!string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase) || !File.Exists(script))
                throw new InvalidOperationException();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-NoProfile -ExecutionPolicy RemoteSigned -File \"{script}\" -Mode Manage",
                UseShellExecute = true, Verb = "runas", WorkingDirectory = root
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot, Title = global::CSIViewer.Core.Localization.Text("Acceso al inicio de Windows"),
                Content = global::CSIViewer.Core.Localization.Text("Instala CSIViewer Host para todos los usuarios en la ruta predeterminada para configurar esta función."),
                CloseButtonText = global::CSIViewer.Core.Localization.Text("Aceptar"), RequestedTheme = ThemeService.Current
            }.ShowAsync();
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
            var success = _unattended.LastOperationSucceeded;
            if (_unattended.LastConfirmedStatus is { } verified) _unattendedStatus = verified;
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
