using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Services;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace CSIViewer.Views;

public sealed class DeviceRow
{
    public required KnownDevice Device { get; init; }
    public required string ListName { get; init; }

    public string Alias => Device.Alias;
    public string DeviceId => Device.DeviceId;
    public string Description => Device.Description;
    public string LastSeenText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Device.PublicKey))
            {
                return "Añadido manualmente";
            }

            if (Device.LastSeenUtc == default)
            {
                return "Sin conexiones";
            }

            var elapsed = DateTimeOffset.UtcNow - Device.LastSeenUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return elapsed.TotalMinutes < 1
                ? "Visto hace un momento"
                : elapsed.TotalHours < 1
                    ? $"Visto hace {elapsed.TotalMinutes:0} min"
                    : elapsed.TotalDays < 1
                        ? $"Visto hace {elapsed.TotalHours:0} h"
                        : $"Visto el {Device.LastSeenUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
        }
    }

    public Visibility FavoriteVisibility => Device.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListVisibility => string.IsNullOrWhiteSpace(ListName) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OnlineVisibility => Device.IsOnline ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OfflineVisibility => Device.IsOnline ? Visibility.Collapsed : Visibility.Visible;
    public string StatusToolTip => Device.IsOnline ? "Conectado" : "Desconectado";
    public Visibility DescriptionVisibility =>
        string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed record DeviceListChoice(string? Id, string Name);

public enum DeviceListFilterKind
{
    All,
    Unfiled,
    List
}

public sealed record DeviceListFilterOption(DeviceListFilterKind Kind, string? ListId, string Name);

public sealed partial class DevicesPage : Page
{
    private const string PasswordEntropy = "CSIViewer.DevicePassword.v1";

    private readonly AppState _state;
    private readonly IKnownDeviceRepository _repository;
    private readonly IDeviceListRepository _listRepository;
    private readonly ObservableCollection<DeviceRow> _filtered = [];
    private bool _suppressFilterChange;
    private bool _dialogOpen;

    public DevicesPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _repository = App.Services.GetRequiredService<IKnownDeviceRepository>();
        _listRepository = App.Services.GetRequiredService<IDeviceListRepository>();
        DevicesList.ItemsSource = _filtered;
        _state.KnownDevices.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(ApplyFilter);
        RefreshListFilter();
        ApplyFilter();
        Loaded += (_, _) =>
        {
            RefreshListFilter();
            ApplyFilter();
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ListFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressFilterChange)
        {
            ApplyFilter();
        }
    }

    private void ConnectHost_Click(object sender, RoutedEventArgs e)
    {
        var target = new string(DirectIdBox.Text.Where(char.IsAsciiDigit).ToArray());
        if (target.Length != 9 || string.IsNullOrWhiteSpace(DirectPasswordBox.Password))
        {
            return;
        }

        RemoteControlPage.PendingTargetId = target;
        RemoteControlPage.PendingPassword = DirectPasswordBox.Password;
        RemoteControlPage.PendingAutoConnect = true;
        DirectPasswordBox.Password = "";
        MainWindow.Instance?.NavigateTo("remote");
    }

    private void RefreshListFilter()
    {
        var current = ListFilterBox.SelectedItem as DeviceListFilterOption;
        var options = new List<DeviceListFilterOption>
        {
            new(DeviceListFilterKind.All, null, "Todas las listas")
        };
        options.AddRange(_state.DeviceLists
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new DeviceListFilterOption(DeviceListFilterKind.List, x.Id, x.Name)));
        options.Add(new DeviceListFilterOption(DeviceListFilterKind.Unfiled, null, "Sin lista"));

        _suppressFilterChange = true;
        ListFilterBox.ItemsSource = options;
        var selected = current is null
            ? options[0]
            : options.FirstOrDefault(x =>
                  x.Kind == current.Kind &&
                  (x.Kind != DeviceListFilterKind.List || x.ListId == current.ListId))
              ?? options[0];
        ListFilterBox.SelectedItem = selected;
        _suppressFilterChange = false;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var filter = ListFilterBox.SelectedItem as DeviceListFilterOption;
        var listNames = _state.DeviceLists
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);

        _filtered.Clear();
        foreach (var device in _state.KnownDevices
                     .OrderByDescending(x => x.IsOnline)
                     .ThenByDescending(x => x.IsFavorite)
                     .ThenByDescending(x => x.LastSeenUtc))
        {
            var listName = device.ListId is not null &&
                           listNames.TryGetValue(device.ListId, out var knownName)
                ? knownName
                : "";

            if (filter?.Kind == DeviceListFilterKind.List && device.ListId != filter.ListId)
            {
                continue;
            }

            if (filter?.Kind == DeviceListFilterKind.Unfiled && listName.Length > 0)
            {
                continue;
            }

            if (query.Length > 0 &&
                !device.Alias.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                !device.DeviceId.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !device.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                !listName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            _filtered.Add(new DeviceRow { Device = device, ListName = listName });
        }

        EmptyState.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceRow row })
        {
            var password = SecretProtector.Unprotect(row.Device.ProtectedPassword, PasswordEntropy);
            RemoteControlPage.PendingTargetId = row.Device.DeviceId;
            RemoteControlPage.PendingPassword = password;
            RemoteControlPage.PendingAutoConnect = !string.IsNullOrWhiteSpace(password);
            MainWindow.Instance?.NavigateTo("remote");
        }
    }

    private async void NewDevice_Click(object sender, RoutedEventArgs e) => await EditDeviceAsync(null);

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceRow row })
        {
            await EditDeviceAsync(row.Device);
        }
    }

    private async Task EditDeviceAsync(KnownDevice? existing)
    {
        var isNew = existing is null;
        var idBox = new TextBox
        {
            Text = existing?.DeviceId ?? "",
            PlaceholderText = "123 456 789",
            MaxLength = 11,
            IsReadOnly = !isNew,
            FontFamily = new FontFamily("Consolas")
        };
        AutomationProperties.SetAutomationId(idBox, "DeviceIdInput");

        var aliasBox = new TextBox
        {
            Text = existing?.Alias ?? "",
            PlaceholderText = "Nombre para reconocerlo, por ejemplo PC de María",
            MaxLength = 80
        };
        AutomationProperties.SetAutomationId(aliasBox, "DeviceAliasInput");

        var descriptionBox = new TextBox
        {
            Text = existing?.Description ?? "",
            PlaceholderText = "Descripción opcional",
            MaxLength = 300,
            AcceptsReturn = true,
            Height = 70
        };
        AutomationProperties.SetAutomationId(descriptionBox, "DeviceDescriptionInput");

        var storedPassword = SecretProtector.Unprotect(existing?.ProtectedPassword, PasswordEntropy);
        var hasStoredPassword = !string.IsNullOrWhiteSpace(existing?.ProtectedPassword);
        var passwordBox = new PasswordBox
        {
            Password = storedPassword ?? "",
            PlaceholderText = "Contraseña del equipo (opcional)",
            MinWidth = 320
        };
        AutomationProperties.SetAutomationId(passwordBox, "DevicePasswordInput");

        var removePasswordBox = new CheckBox
        {
            Content = "Quitar la contraseña guardada",
            Visibility = hasStoredPassword ? Visibility.Visible : Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(removePasswordBox, "DeviceRemovePasswordBox");

        var listChoices = new List<DeviceListChoice> { new(null, "Sin lista") };
        listChoices.AddRange(_state.DeviceLists
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new DeviceListChoice(x.Id, x.Name)));
        var listBox = new ComboBox
        {
            ItemsSource = listChoices,
            DisplayMemberPath = "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        listBox.SelectedItem = listChoices.FirstOrDefault(x => x.Id == existing?.ListId) ?? listChoices[0];
        AutomationProperties.SetAutomationId(listBox, "DeviceListChoice");

        var favoriteBox = new CheckBox
        {
            Content = "Favorito",
            IsChecked = existing?.IsFavorite == true
        };
        AutomationProperties.SetAutomationId(favoriteBox, "DeviceFavoriteBox");

        var errorText = new TextBlock
        {
            Foreground = (Brush)XamlApplication.Current.Resources["DangerBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock { Text = "ID del dispositivo (9 dígitos)" });
        content.Children.Add(idBox);
        content.Children.Add(new TextBlock { Text = "Nombre" });
        content.Children.Add(aliasBox);
        content.Children.Add(new TextBlock { Text = "Descripción" });
        content.Children.Add(descriptionBox);
        content.Children.Add(new TextBlock { Text = "Contraseña del equipo" });
        content.Children.Add(passwordBox);
        content.Children.Add(new TextBlock
        {
            Text = hasStoredPassword
                ? storedPassword is null
                    ? "Hay una contraseña guardada que Windows no puede descifrar con este usuario. Se conservará tal cual; escribe una nueva para reemplazarla."
                    : "Se guarda cifrada con DPAPI para este usuario de Windows. Déjala vacía para mantener la contraseña actual."
                : "Se guarda cifrada con DPAPI para este usuario de Windows. Déjala vacía para no guardarla.",
            Style = GetStyle("MutedTextStyle"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(removePasswordBox);
        content.Children.Add(new TextBlock { Text = "Lista" });
        content.Children.Add(listBox);
        content.Children.Add(new TextBlock
        {
            Text = "Puedes crear, renombrar o eliminar listas desde el botón «Listas» de la página.",
            Style = GetStyle("MutedTextStyle"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(favoriteBox);
        content.Children.Add(errorText);

        void ShowError(string message)
        {
            errorText.Text = message;
            errorText.Visibility = Visibility.Visible;
        }

        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = isNew ? "Nuevo dispositivo" : $"Editar dispositivo {existing!.DeviceId}",
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var deviceId = new string(idBox.Text.Where(char.IsAsciiDigit).ToArray());
            if (deviceId.Length != 9)
            {
                ShowError("El ID debe contener exactamente 9 dígitos.");
                args.Cancel = true;
                return;
            }

            if (isNew && _state.KnownDevices.Any(x => x.DeviceId == deviceId))
            {
                ShowError("Ya existe un dispositivo con ese ID. Edítalo desde la lista.");
                args.Cancel = true;
            }
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var id = new string(idBox.Text.Where(char.IsAsciiDigit).ToArray());
        var alias = aliasBox.Text.Trim();
        var description = descriptionBox.Text.Trim();
        var listChoice = listBox.SelectedItem as DeviceListChoice;
        var listId = string.IsNullOrWhiteSpace(listChoice?.Id) ? null : listChoice.Id;
        var password = passwordBox.Password;
        var protectedPassword = removePasswordBox.IsChecked == true
            ? null
            : string.IsNullOrWhiteSpace(password)
                ? existing?.ProtectedPassword
                : SecretProtector.Protect(password, PasswordEntropy);
        var device = new KnownDevice(
            id,
            alias.Length == 0 ? id : alias,
            description,
            "",
            favoriteBox.IsChecked == true,
            existing?.IsOnline == true,
            existing?.LastSeenUtc ?? DateTimeOffset.UtcNow,
            existing?.PublicKey ?? "",
            listId,
            protectedPassword);

        try
        {
            await _repository.UpsertAsync(device);
            if (existing is null)
            {
                _state.KnownDevices.Add(device);
            }
            else
            {
                var index = _state.KnownDevices.IndexOf(existing);
                if (index >= 0)
                {
                    _state.KnownDevices[index] = device;
                }
                else
                {
                    _state.KnownDevices.Add(device);
                }
            }

            RefreshListFilter();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("No se pudo guardar el dispositivo", ex.Message);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DeviceRow row })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = "Eliminar dispositivo",
            Content = $"¿Quieres quitar «{row.Alias}» ({row.DeviceId}) de tus dispositivos guardados?\n\nNo se desinstala nada en el equipo remoto.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _repository.DeleteAsync(row.Device.DeviceId);
            _state.KnownDevices.Remove(row.Device);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("No se pudo eliminar el dispositivo", ex.Message);
        }
    }

    private async void ManageLists_Click(object sender, RoutedEventArgs e) => await ManageListsAsync();

    private async Task ManageListsAsync()
    {
        var errorText = new TextBlock
        {
            Foreground = (Brush)XamlApplication.Current.Resources["DangerBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var newNameBox = new TextBox
        {
            PlaceholderText = "Nueva lista, por ejemplo Clientes",
            MaxLength = 40
        };
        AutomationProperties.SetAutomationId(newNameBox, "NewListNameBox");

        var createButton = new Button
        {
            Content = "Crear lista",
            Style = GetStyle("PrimaryButtonStyle")
        };
        AutomationProperties.SetAutomationId(createButton, "CreateListButton");

        var createRow = new Grid { ColumnSpacing = 8 };
        createRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        createRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        createRow.Children.Add(newNameBox);
        Grid.SetColumn(createButton, 1);
        createRow.Children.Add(createButton);

        var listsPanel = new StackPanel { Spacing = 8 };
        var listsScroll = new ScrollViewer
        {
            Content = listsPanel,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Organiza tus equipos en listas. Los dispositivos de una lista se pueden filtrar y siguen guardados aunque elimines la lista.",
            Style = GetStyle("MutedTextStyle"),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(createRow);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)XamlApplication.Current.Resources["CardStrokeBrush"],
            Margin = new Thickness(0, 4, 0, 4)
        });
        content.Children.Add(listsScroll);
        content.Children.Add(errorText);

        string? pendingDeleteId = null;

        void ShowError(string? message)
        {
            errorText.Text = message ?? "";
            errorText.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        bool ValidateName(string name, string? exceptId, out string error)
        {
            if (name.Length == 0)
            {
                error = "Escribe un nombre para la lista.";
                return false;
            }

            if (name.Length > 40)
            {
                error = "El nombre no puede superar los 40 caracteres.";
                return false;
            }

            if (_state.DeviceLists.Any(x =>
                    x.Id != exceptId &&
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Ya existe una lista con ese nombre.";
                return false;
            }

            error = "";
            return true;
        }

        void Rebuild()
        {
            listsPanel.Children.Clear();
            if (_state.DeviceLists.Count == 0)
            {
                listsPanel.Children.Add(new TextBlock
                {
                    Text = "Todavía no hay listas. Crea una arriba, por ejemplo «Clientes» o «Casa».",
                    Style = GetStyle("MutedTextStyle"),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var list in _state.DeviceLists
                         .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                         .ToList())
            {
                var count = _state.KnownDevices.Count(x => x.ListId == list.Id);
                var nameBox = new TextBox
                {
                    Text = list.Name,
                    MaxLength = 40,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var renameButton = new Button
                {
                    Content = "Guardar nombre",
                    Style = GetStyle("SecondaryButtonStyle")
                };
                var deleteButton = new Button
                {
                    Content = pendingDeleteId == list.Id ? "Confirmar" : "Eliminar",
                    Style = GetStyle("DangerButtonStyle")
                };

                renameButton.Click += async (_, _) =>
                {
                    var name = nameBox.Text.Trim();
                    if (!ValidateName(name, list.Id, out var error))
                    {
                        ShowError(error);
                        return;
                    }

                    try
                    {
                        var updated = list with { Name = name };
                        await _listRepository.UpsertAsync(updated);
                        ReplaceList(updated);
                        ShowError(null);
                        RefreshListFilter();
                        ApplyFilter();
                        Rebuild();
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex.Message);
                    }
                };

                deleteButton.Click += async (_, _) =>
                {
                    if (pendingDeleteId != list.Id)
                    {
                        pendingDeleteId = list.Id;
                        ShowError(count > 0
                            ? $"La lista «{list.Name}» tiene {count} dispositivo(s). Si la eliminas, quedarán como «Sin lista»."
                            : null);
                        Rebuild();
                        return;
                    }

                    try
                    {
                        await _listRepository.DeleteAsync(list.Id);
                        _state.DeviceLists.Remove(list);
                        for (var i = 0; i < _state.KnownDevices.Count; i++)
                        {
                            var device = _state.KnownDevices[i];
                            if (device.ListId == list.Id)
                            {
                                _state.KnownDevices[i] = device with { ListId = null, GroupName = "" };
                            }
                        }

                        pendingDeleteId = null;
                        ShowError(null);
                        RefreshListFilter();
                        ApplyFilter();
                        Rebuild();
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex.Message);
                    }
                };

                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(nameBox);

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                actions.Children.Add(renameButton);
                actions.Children.Add(deleteButton);
                Grid.SetColumn(actions, 1);
                row.Children.Add(actions);

                var rowHost = new StackPanel { Spacing = 2 };
                rowHost.Children.Add(row);
                rowHost.Children.Add(new TextBlock
                {
                    Text = count == 1 ? "1 dispositivo" : $"{count} dispositivos",
                    Style = GetStyle("MutedTextStyle"),
                    FontSize = 11
                });
                listsPanel.Children.Add(rowHost);
            }
        }

        createButton.Click += async (_, _) =>
        {
            var name = newNameBox.Text.Trim();
            if (!ValidateName(name, null, out var error))
            {
                ShowError(error);
                return;
            }

            try
            {
                var list = new DeviceList(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow);
                await _listRepository.UpsertAsync(list);
                _state.DeviceLists.Add(list);
                newNameBox.Text = "";
                ShowError(null);
                RefreshListFilter();
                ApplyFilter();
                Rebuild();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        };

        Rebuild();

        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = "Listas de dispositivos",
            CloseButtonText = "Cerrar",
            DefaultButton = ContentDialogButton.Close,
            Content = content
        };

        await ShowDialogAsync(dialog);
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        // WinUI allows only one ContentDialog at a time. Returning None also
        // keeps fast double-clicks or programmatic input from crashing the app.
        if (_dialogOpen)
        {
            return ContentDialogResult.None;
        }

        _dialogOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private void ReplaceList(DeviceList updated)
    {
        for (var i = 0; i < _state.DeviceLists.Count; i++)
        {
            if (_state.DeviceLists[i].Id == updated.Id)
            {
                _state.DeviceLists[i] = updated;
                return;
            }
        }

        _state.DeviceLists.Add(updated);
    }

    private static Style GetStyle(string key) => (Style)XamlApplication.Current.Resources[key];

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = ThemeService.Current,
            XamlRoot = MainWindow.Instance?.Content?.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Aceptar",
            DefaultButton = ContentDialogButton.Close
        };
        await ShowDialogAsync(dialog);
    }
}
