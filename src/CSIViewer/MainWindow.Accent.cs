using CSIViewer.Core;
using CSIViewer.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

#if CSIVIEWER_HOST
namespace CSIViewer.Host;
#else
namespace CSIViewer;
#endif

public sealed partial class MainWindow
{
    private void InitializeAccentMenu()
    {
        foreach (var choice in AccentPalette.Choices)
        {
            var item = new ToggleMenuFlyoutItem { Text = choice.Name, Tag = choice.Value };
            item.Click += AccentMenuItem_Click;
            AccentMenu.Items.Add(item);
        }
        AccentMenu.Opening += (_, _) =>
        {
            foreach (var item in AccentMenu.Items.OfType<ToggleMenuFlyoutItem>())
            {
                item.IsChecked = item.Tag is AccentColor color && color == AccentPalette.Normalize(_state.Settings.AccentColor);
                if (item.Tag is AccentColor value) item.Text = AccentPalette.Choices.First(x => x.Value == value).Name;
            }
        };
    }

    private async void AccentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem { Tag: AccentColor color }) return;
        _state.Settings.AccentColor = color;
        AccentAppearance.Apply(color, RootGrid);
        try { await _settingsStore.SaveAsync(_state.Settings); }
        catch (Exception)
        {
            _state.LastError = global::CSIViewer.Core.Localization.Text("El color se ha aplicado, pero no se pudo guardar para el próximo inicio.");
        }
    }
}
