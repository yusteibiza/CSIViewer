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
        AccentAppearance.Changed += (_, _) => DispatcherQueue.TryEnqueue(SynchronizeAccentSelection);
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
        AccentAppearance.Apply(choice.Value, MainWindow.Instance?.Content as FrameworkElement);
        try { await _settings.SaveAsync(_state.Settings); }
        catch (Exception) { ShowStatus(InfoBarSeverity.Warning, "Apariencia", "El color se ha aplicado, pero no se pudo guardar."); }
    }
}
