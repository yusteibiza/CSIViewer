using CSIViewer.Core;
using CSIViewer.Application;
using CSIViewer.Services;
using CSIViewer.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

#if CSIVIEWER_HOST
using CSIViewer.Host.Services;
namespace CSIViewer.Host.Views;
#else
namespace CSIViewer.Views;
#endif

public sealed partial class HostSettingsPage
{
    private void ConfigurationHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 850;
        if (sender is Grid grid)
        {
            var rows = narrow ? 2 : 1;
            while (grid.RowDefinitions.Count < rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            while (grid.RowDefinitions.Count > rows) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        }
        Grid.SetRow(ConfigurationActions, narrow ? 1 : 0);
        Grid.SetColumn(ConfigurationActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(ConfigurationActions, narrow ? 2 : 1);
    }

    private void InitializeAccentOptions()
    {
        AccentColorCombo.ItemsSource = AccentPalette.Choices;
        LanguageCombo.ItemsSource = Localization.Languages;
    }

    private void SynchronizeAccentSelection(AppSettings? settings = null)
    {
        settings ??= _state.Settings;
        var loading = _loading;
        _loading = true;
        AccentColorCombo.SelectedItem = AccentPalette.Choices.First(x => x.Value == AccentPalette.Normalize(settings.AccentColor));
        LanguageCombo.SelectedItem = Localization.Languages.First(x => x.Code == Localization.Normalize(settings.Language));
        _loading = loading;
    }

    private void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private bool _savingConfiguration;

    private void RestoreSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_savingConfiguration) { LoadSettings(); StatusBar.IsOpen = false; }
    }

    private void DefaultSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_savingConfiguration) { LoadSettings(new AppSettings()); StatusBar.IsOpen = false; }
    }

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (_savingConfiguration) return;
        var settings = _state.Settings.Clone();
        settings.ServerUrl = ServerUrlBox.Text.Trim();
        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowStatus(InfoBarSeverity.Warning, Localization.Text("URL no válida"), Localization.Text("Escribe una dirección http:// o https:// completa, por ejemplo http://localhost:5080."));
            return;
        }
        settings.Theme = ThemeCombo.SelectedIndex switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.System };
        settings.AccentColor = (AccentColorCombo.SelectedItem as AccentChoice)?.Value ?? settings.AccentColor;
        settings.Language = (LanguageCombo.SelectedItem as LanguageChoice)?.Code ?? settings.Language;
        settings.ImageQuality = (int)QualitySlider.Value;
        settings.Fps = FpsCombo.SelectedItem is int fps ? fps : settings.Fps;
        settings.MaxWidth = (int)MaxWidthSlider.Value;
        settings.PreferH264 = H264Switch.IsOn;
        settings.PreferModernCapture = ModernCaptureSwitch.IsOn;
#if CSIVIEWER_HOST
        settings.AllowInsecureServer = AllowInsecureSwitch.IsOn;
        settings.DownloadFolder = DownloadFolderBox.Text.Trim();
        settings.HostConsentMode = ConsentModeCombo.SelectedIndex switch
        {
            1 => HostConsentMode.AcceptAutomatically, 2 => HostConsentMode.RejectAutomatically, _ => HostConsentMode.Ask
        };
#endif
        settings.Normalize();
        var reconnect = settings.ServerUrl != _state.Settings.ServerUrl || settings.AllowInsecureServer != _state.Settings.AllowInsecureServer;
        _savingConfiguration = true;
        foreach (var button in ConfigurationActions.Children.OfType<Button>()) button.IsEnabled = false;
        try
        {
            await _settings.SaveAsync(settings);
            _state.Settings = settings;
            ThemeService.Apply(settings.Theme);
            AccentAppearance.Apply(settings.AccentColor, MainWindow.Instance?.Content as FrameworkElement);
            if (MainWindow.Instance?.Content is FrameworkElement root) Translate.Apply(root, settings.Language);
            LoadSettings();
            ShowStatus(InfoBarSeverity.Success, Localization.Text("Configuración"), Localization.Text("Configuración guardada."));
            try
            {
                await _client.RestartHostCaptureAsync();
                if (reconnect) { await _client.DisconnectFromServerAsync(); await _client.ConnectToServerAsync(); }
            }
            catch (Exception ex) { ShowStatus(InfoBarSeverity.Warning, Localization.Text("Configuración guardada"), ex.GetBaseException().Message); }
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Error, Localization.Text("Configuración"), Localization.Text("No se pudo guardar la configuración: ") + ex.GetBaseException().Message); }
        finally { _savingConfiguration = false; foreach (var button in ConfigurationActions.Children.OfType<Button>()) button.IsEnabled = true; }
    }
}
