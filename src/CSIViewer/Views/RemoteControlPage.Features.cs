using CSIViewer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSIViewer.Views;

public sealed partial class RemoteControlPage
{
    private void RefreshFeatureControls()
    {
        var enabled = _state.IsSessionActive && _state.IsController;
        SessionAudioBox.IsEnabled = enabled && _state.ActivePermissions.HasFlag(SessionPermissions.Audio);
        SessionClipboardBox.IsEnabled = enabled && _state.ActivePermissions.HasFlag(SessionPermissions.Clipboard);
        SessionAudioBox.IsChecked = enabled && _client.IsAudioEnabled;
        SessionClipboardBox.IsChecked = enabled && _client.IsClipboardEnabled;
    }

    private async void SessionFeatures_Click(object sender, RoutedEventArgs e)
    {
        SessionAudioBox.IsEnabled = SessionClipboardBox.IsEnabled = false;
        try { await _client.ConfigureSessionFeaturesAsync(SessionAudioBox.IsChecked == true, SessionClipboardBox.IsChecked == true); }
        catch (Exception)
        {
            StatusBar.Message = Localization.Text("No se pudieron activar el audio y el portapapeles.");
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.IsOpen = true;
        }
        finally { RefreshFeatureControls(); }
    }
}
