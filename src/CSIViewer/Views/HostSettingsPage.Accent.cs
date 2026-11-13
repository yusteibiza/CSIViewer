using CSIViewer.Core;
using CSIViewer.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

#if CSIVIEWER_HOST
namespace CSIViewer.Host.Views;
#else
namespace CSIViewer.Views;
#endif

public sealed partial class HostSettingsPage
{
    private void InitializeAccentOptions()
    {
        AccentColorCombo.ItemsSource = AccentPalette.Choices;
        LanguageCombo.ItemsSource = Localization.Languages;
        AccentAppearance.Changed += (_, _) => DispatcherQueue.TryEnqueue(SynchronizeAccentSelection);
    }

    private void SynchronizeAccentSelection()
    {
        var loading = _loading;
        _loading = true;
        AccentColorCombo.SelectedItem = AccentPalette.Choices.First(x => x.Value == AccentPalette.Normalize(_state.Settings.AccentColor));
        LanguageCombo.SelectedItem = Localization.Languages.First(x => x.Code == Localization.Normalize(_state.Settings.Language));
        _loading = loading;
    }

    private async void AccentColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentColorCombo.SelectedItem is not AccentChoice choice) return;
        _state.Settings.AccentColor = choice.Value;
        AccentAppearance.Apply(choice.Value, MainWindow.Instance?.Content as FrameworkElement);
        try { await _settings.SaveAsync(_state.Settings); }
        catch (Exception) { ShowStatus(InfoBarSeverity.Warning, global::CSIViewer.Core.Localization.Text("Apariencia"), global::CSIViewer.Core.Localization.Text("El color se ha aplicado, pero no se pudo guardar.")); }
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not LanguageChoice choice) return;
        _state.Settings.Language = choice.Code;
        if (MainWindow.Instance?.Content is FrameworkElement root) Translate.Apply(root, choice.Code);
        try { await _settings.SaveAsync(_state.Settings); }
        catch (Exception) { ShowStatus(InfoBarSeverity.Warning, Localization.Text("Idioma"), Localization.Text("No se pudo guardar el idioma.")); }
    }
}
