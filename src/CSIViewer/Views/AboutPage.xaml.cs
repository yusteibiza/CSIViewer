using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Services;

namespace CSIViewer.Views;

public sealed partial class AboutPage : Page
{
    private readonly AppState _state;

    public AboutPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        Loaded += (_, _) =>
        {
            AboutDeviceIdText.Text = "ID: " + _state.DeviceId;
            AboutFingerprintText.Text = "Huella: " + (_state.Fingerprint == "" ? "—" : _state.Fingerprint);
            AboutServerText.Text = global::CSIViewer.Core.Localization.Text("Servidor: ") + _state.Settings.ServerUrl + " · " + _state.ServerStatus;
        };
    }
}
