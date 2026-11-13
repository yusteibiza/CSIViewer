using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Services;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace CSIViewer.Views;

public sealed class ContactRow
{
    public required Contact Contact { get; init; }
    public required ObservableCollection<KnownDevice> Devices { get; init; }
    public string Name => Contact.DisplayName;
    public string Notes => Contact.Notes;
    public Visibility EmptyVisibility => Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class ContactsPage : Page
{
    private readonly AppState _state;
    private readonly IContactRepository _repository;
    private readonly ObservableCollection<ContactRow> _rows = [];

    public ContactsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _repository = App.Services.GetRequiredService<IContactRepository>();
        ContactsList.ItemsSource = _rows;
        Loaded += (_, _) => RefreshRows();
    }

    private void RefreshRows()
    {
        _rows.Clear();
        foreach (var contact in _state.Contacts.OrderBy(x => x.DisplayName))
        {
            var devices = new ObservableCollection<KnownDevice>(
                (contact.DeviceIds ?? [])
                    .Select(id => _state.KnownDevices.FirstOrDefault(x => x.DeviceId == id))
                    .Where(x => x is not null)
                    .Select(x => x!));
            _rows.Add(new ContactRow { Contact = contact, Devices = devices });
        }
    }

    private async void AddContact_Click(object sender, RoutedEventArgs e)
    {
        await EditContactAsync(null);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ContactRow row })
        {
            await EditContactAsync(row.Contact);
        }
    }

    private async Task EditContactAsync(Contact? existing)
    {
        var nameBox = new TextBox
        {
            Text = existing?.DisplayName ?? "",
            PlaceholderText = "Nombre del contacto",
            MaxLength = 80
        };
        var notesBox = new TextBox
        {
            Text = existing?.Notes ?? "",
            PlaceholderText = "Notas",
            AcceptsReturn = true,
            MaxLength = 500,
            Height = 70
        };

        var devicePanel = new StackPanel { Spacing = 4 };
        var checks = new List<(string DeviceId, CheckBox Box)>();
        foreach (var device in _state.KnownDevices.OrderBy(x => x.Alias))
        {
            var box = new CheckBox
            {
                Content = $"{device.Alias} ({device.DeviceId})",
                IsChecked = existing?.DeviceIds?.Contains(device.DeviceId) == true,
                Tag = device.DeviceId
            };
            checks.Add((device.DeviceId, box));
            devicePanel.Children.Add(box);
        }

        if (checks.Count == 0)
        {
            devicePanel.Children.Add(new TextBlock
            {
                Text = "Todavía no hay dispositivos guardados. Añade uno desde Dispositivos.",
                Style = (Style)XamlApplication.Current.Resources["MutedTextStyle"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "Nombre" });
        content.Children.Add(nameBox);
        content.Children.Add(new TextBlock { Text = "Notas" });
        content.Children.Add(notesBox);
        content.Children.Add(new TextBlock { Text = "Dispositivos vinculados" });
        content.Children.Add(new ScrollViewer
        {
            Content = devicePanel,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = existing is null ? "Nuevo contacto" : "Editar contacto",
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        var contact = new Contact(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            nameBox.Text.Trim(),
            notesBox.Text.Trim(),
            existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
            checks.Where(x => x.Box.IsChecked == true).Select(x => x.DeviceId).ToArray());

        await _repository.UpsertAsync(contact);
        var index = _state.Contacts.ToList().FindIndex(x => x.Id == contact.Id);
        if (index >= 0)
        {
            _state.Contacts[index] = contact;
        }
        else
        {
            _state.Contacts.Add(contact);
        }

        RefreshRows();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ContactRow row })
        {
            return;
        }

        await _repository.DeleteAsync(row.Contact.Id);
        _state.Contacts.Remove(row.Contact);
        RefreshRows();
    }

    private void ConnectDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KnownDevice device })
        {
            RemoteControlPage.PendingTargetId = device.DeviceId;
            RemoteControlPage.PendingPassword = null;
            RemoteControlPage.PendingAutoConnect = false;
            MainWindow.Instance?.NavigateTo("remote");
        }
    }
}
