using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Services;
using CSIViewer.Host.Services;
using CSIViewer.Presentation;

namespace CSIViewer.Host.Views;

public sealed class ContactRow
{
    public required Contact Contact { get; init; }
    public required ObservableCollection<KnownDevice> Devices { get; init; }
    public string Name => Contact.DisplayName;
    public string Notes => Contact.Notes;
    public string Details => string.Join(" · ", new[] { Contact.Company, Contact.Email, Contact.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public Visibility DetailsVisibility => string.IsNullOrWhiteSpace(Details) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NotesVisibility => string.IsNullOrWhiteSpace(Notes) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FavoriteVisibility => Contact.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class ContactsPage : Page
{
    private readonly AppState _state;
    private readonly IContactRepository _repository;
    private readonly ObservableCollection<ContactRow> _rows = [];
    private int _refreshQueued;

    public ContactsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _repository = App.Services.GetRequiredService<IContactRepository>();
        ContactsList.ItemsSource = _rows;
        _state.Contacts.CollectionChanged += (_, _) => QueueRefresh();
        _state.KnownDevices.CollectionChanged += (_, _) => QueueRefresh();
        Loaded += (_, _) => RefreshRows();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() => { Interlocked.Exchange(ref _refreshQueued, 0); RefreshRows(); }))
            Interlocked.Exchange(ref _refreshQueued, 0);
    }

    private void RefreshRows()
    {
        _rows.Clear();
        foreach (var contact in _state.Contacts.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.DisplayName))
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
        var contact = await ContactEditorDialog.ShowAsync(existing, _state.KnownDevices, XamlRoot, ThemeService.Current);
        if (contact is null) return;

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
            var password = SecretProtector.Unprotect(device.ProtectedPassword, "CSIViewer.DevicePassword.v1");
            RemoteControlPage.PendingTargetId = device.DeviceId;
            RemoteControlPage.PendingPassword = password;
            RemoteControlPage.PendingAutoConnect = !string.IsNullOrWhiteSpace(password);
            MainWindow.Instance?.NavigateTo("remote");
        }
    }
}
