using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Services;
using CSIViewer.Host.Services;
using Windows.ApplicationModel.DataTransfer;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace CSIViewer.Host.Views;

public sealed partial class HostPage : Page
{
    private readonly AppState _state;
    private readonly IdentityService _identity;
    private readonly RemoteClient _client;
    private readonly ISettingsStore _settings;
    private bool _showPassword = true;
    private bool _updatingToggle;
    private int _refreshScheduled;

    public HostPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _identity = App.Services.GetRequiredService<IdentityService>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _settings = App.Services.GetRequiredService<ISettingsStore>();

        _state.PropertyChanged += State_PropertyChanged;
        Loaded += (_, _) => Refresh();
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshScheduled, 1) != 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _refreshScheduled, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        DeviceIdText.Text = string.IsNullOrWhiteSpace(_state.DeviceId) ? "-" : _state.DeviceId;
        PasswordText.Text = _showPassword
            ? (_state.TemporaryPasswordDisplay == "-" ? "-" : _state.TemporaryPasswordDisplay)
            : "••••••••";
        PasswordExpiryText.Text = _state.UsesCustomPassword
            ? global::CSIViewer.Core.Localization.Text("Contraseña propia (sin caducidad).")
            : _state.TemporaryPasswordExpiresUtc is { } expiry
                ? $"Caduca: {expiry.ToLocalTime():dd/MM/yyyy HH:mm} (hora local)."
                : global::CSIViewer.Core.Localization.Text("Sin caducidad configurada.");

        var online = _state.IsServerOnline;
        StatusDot.Fill = (Brush)XamlApplication.Current.Resources[online ? "OnlineBrush" : "OfflineBrush"];
        StatusText.Text = online ? global::CSIViewer.Core.Localization.Text("Listo para conectar") : global::CSIViewer.Core.Localization.Text("Sin conexión");
        StatusDetailText.Text = online
            ? global::CSIViewer.Core.Localization.Text("El equipo está disponible para recibir una sesión.")
            : global::CSIViewer.Core.Localization.Text("Sin conexión con el servidor. Reintentando…");

        _updatingToggle = true;
        AllowIncomingSwitch.IsOn = _state.Settings.AllowIncomingConnections;
        _updatingToggle = false;

        ChatButton.IsEnabled = _state.IsSessionActive && _state.ActivePermissions.HasFlag(SessionPermissions.Chat);
        if (_state.IsSessionActive)
        {
            SessionText.Text = _state.IsController
                ? $"Controlando {_state.RemoteDeviceName} ({_state.RemoteDeviceId})."
                : global::CSIViewer.Core.Localization.Format("Sesión remota de {0} ({1}).",_state.RemoteDeviceName,_state.RemoteDeviceId);
            EndSessionButton.IsEnabled = true;
        }
        else
        {
            SessionText.Text = global::CSIViewer.Core.Localization.Text("No hay ninguna sesión activa.");
            EndSessionButton.IsEnabled = false;
        }
    }

    private void CopyId_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(_state.DeviceId);
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(_state.TemporaryPasswordRaw);
    }

    private void RevealPassword_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        Refresh();
    }

    private async void RegeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        await _identity.RegeneratePasswordAsync();
        Refresh();
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var passwordBox = new PasswordBox { PlaceholderText = global::CSIViewer.Core.Localization.Text("Nueva contraseña"), MinWidth = 320 };
        var confirmBox = new PasswordBox { PlaceholderText = global::CSIViewer.Core.Localization.Text("Repite la contraseña"), MinWidth = 320 };
        var persistBox = new CheckBox
        {
            Content = global::CSIViewer.Core.Localization.Text("Mantener esta contraseña al reiniciar"),
            IsChecked = true
        };
        var errorText = new TextBlock
        {
            Foreground = (Brush)XamlApplication.Current.Resources["DangerBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = global::CSIViewer.Core.Localization.Text("Contraseña (8-64 caracteres)") });
        content.Children.Add(passwordBox);
        content.Children.Add(confirmBox);
        content.Children.Add(persistBox);
        content.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = global::CSIViewer.Core.Localization.Text("Cambiar contraseña temporal"),
            PrimaryButtonText = global::CSIViewer.Core.Localization.Text("Guardar"),
            CloseButtonText = global::CSIViewer.Core.Localization.Text("Cancelar"),
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (passwordBox.Password != confirmBox.Password)
        {
            await ShowMessageAsync(global::CSIViewer.Core.Localization.Text("Las contraseñas no coinciden."));
            return;
        }

        try
        {
            await _identity.ChangePasswordAsync(passwordBox.Password, persistBox.IsChecked == true);
            Refresh();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(ex.Message);
        }
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = global::CSIViewer.Core.Localization.Text("Contraseña no válida"),
            Content = message,
            CloseButtonText = "Aceptar",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async void AllowIncoming_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingToggle)
        {
            return;
        }

        _state.Settings.AllowIncomingConnections = AllowIncomingSwitch.IsOn;
        await _settings.SaveAsync(_state.Settings);
        Refresh();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo("host-settings");
    }

    private void OpenHelp_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo("help");
    }

    private void OpenChat_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo("chat");
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        await _client.EndCurrentSessionsAsync();
    }

    private static void CopyToClipboard(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(value);
        Clipboard.SetContent(package);
    }
}
