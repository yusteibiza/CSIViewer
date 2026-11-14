using System.Net.Mail;
using CSIViewer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CSIViewer.Presentation;

/// <summary>Shared contact editor, compiled independently into viewer and host.</summary>
public static class ContactEditorDialog
{
    public static async Task<Contact?> ShowAsync(Contact? existing, IEnumerable<KnownDevice> devices, XamlRoot xamlRoot, ElementTheme theme)
    {
        var bodyWidth = Math.Min(480, Math.Max(200, xamlRoot.Size.Width - 96));
        var name = Field("ContactNameInput", Localization.Text("Nombre"), existing?.DisplayName, 80);
        var company = Field("ContactCompanyInput", Localization.Text("Empresa"), existing?.Company, 120);
        var email = Field("ContactEmailInput", Localization.Text("Correo electrónico"), existing?.Email, 160);
        var phone = Field("ContactPhoneInput", Localization.Text("Teléfono"), existing?.Phone, 40);
        email.InputScope = new Microsoft.UI.Xaml.Input.InputScope
        {
            Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.EmailSmtpAddress) }
        };
        var notes = Field("ContactNotesInput", Localization.Text("Notas"), existing?.Notes, 500);
        notes.AcceptsReturn = true; notes.TextWrapping = TextWrapping.Wrap; notes.MinHeight = 76; notes.MaxHeight = 140;
        var favorite = new CheckBox { Content = Localization.Text("Marcar como favorito"), IsChecked = existing?.IsFavorite == true };
        AutomationProperties.SetAutomationId(favorite, "ContactFavoriteBox");

        var choices = devices.ToDictionary(x => x.DeviceId, x => $"{x.Alias} ({x.DeviceId})", StringComparer.Ordinal);
        foreach (var id in existing?.DeviceIds ?? [])
            choices.TryAdd(id, Localization.Format("Dispositivo no guardado ({0})", id));
        var deviceList = new ListView { SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, MaxHeight = 220 };
        AutomationProperties.SetAutomationId(deviceList, "ContactDeviceChoices");
        foreach (var choice in choices.OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ListViewItem { Content = new TextBlock { Text = choice.Value, TextWrapping = TextWrapping.Wrap }, Tag = choice.Key };
            AutomationProperties.SetName(item, choice.Value);
            AutomationProperties.SetAutomationId(item, "ContactDevice-" + choice.Key);
            deviceList.Items.Add(item);
            if (existing?.DeviceIds?.Contains(choice.Key) == true) deviceList.SelectedItems.Add(item);
        }
        var summary = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 400 };
        var picker = new DropDownButton { Content = summary, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(picker, "ContactDevicesCombo");
        AutomationProperties.SetName(picker, Localization.Text("Dispositivos vinculados"));
        void RefreshSummary()
        {
            summary.Text = deviceList.SelectedItems.Count == 0 ? Localization.Text("Seleccionar dispositivos…")
                : deviceList.SelectedItems.Count == 1 ? Localization.Text("1 dispositivo seleccionado")
                : Localization.Format("{0} dispositivos seleccionados", deviceList.SelectedItems.Count);
        }
        deviceList.SelectionChanged += (_, _) => RefreshSummary();
        RefreshSummary();
        var search = Field("ContactDeviceSearch", "", "", 100);
        search.PlaceholderText = Localization.Text("Buscar por nombre o ID…");
        search.TextChanged += (_, _) =>
        {
            foreach (ListViewItem item in deviceList.Items)
                item.Visibility = choices[(string)item.Tag].Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        };
        var clear = new Button { Content = Localization.Text("Desvincular todos"), Style = Resource<Style>("SecondaryButtonStyle") };
        AutomationProperties.SetAutomationId(clear, "ContactClearDevicesButton");
        clear.Click += (_, _) => deviceList.SelectedItems.Clear();
        var flyoutContent = new StackPanel { Spacing = 8, Width = Math.Min(440, Math.Max(220, xamlRoot.Size.Width - 120)) };
        flyoutContent.Children.Add(search);
        flyoutContent.Children.Add(new TextBlock { Text = Localization.Text("Puedes seleccionar varios equipos."), TextWrapping = TextWrapping.Wrap, Style = Resource<Style>("MutedTextStyle") });
        flyoutContent.Children.Add(deviceList);
        flyoutContent.Children.Add(clear);
        picker.Flyout = new Flyout { Content = flyoutContent, Placement = FlyoutPlacementMode.Bottom };
        picker.IsEnabled = choices.Count > 0;

        var details = new StackPanel { Spacing = 12 };
        details.Children.Add(name); details.Children.Add(company);
        var channels = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        if (bodyWidth >= 440)
        {
            channels.ColumnDefinitions.Add(new ColumnDefinition()); channels.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(phone, 1);
        }
        else
        {
            channels.RowDefinitions.Add(new RowDefinition()); channels.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(phone, 1);
        }
        channels.Children.Add(email); channels.Children.Add(phone);
        details.Children.Add(channels); details.Children.Add(favorite);
        var linked = new StackPanel { Spacing = 8 };
        linked.Children.Add(new TextBlock { Text = Localization.Text("Dispositivos vinculados"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        linked.Children.Add(picker);
        if (choices.Count == 0) linked.Children.Add(new TextBlock
        {
            Text = Localization.Text("Todavía no hay dispositivos guardados. Añade uno desde Dispositivos."),
            TextWrapping = TextWrapping.Wrap, Style = Resource<Style>("MutedTextStyle")
        });
        var error = new InfoBar { IsOpen = false, Severity = InfoBarSeverity.Error, IsClosable = false };
        AutomationProperties.SetAutomationId(error, "ContactValidationError");
        name.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text) && error.Message == Localization.Text("Introduce un nombre para el contacto."))
                error.IsOpen = false;
        };
        email.TextChanged += (_, _) =>
        {
            if (IsValidEmail(email.Text) && error.Message == Localization.Text("Revisa el correo electrónico."))
                error.IsOpen = false;
        };
        var content = new StackPanel { Spacing = 12, Width = bodyWidth };
        content.Children.Add(new TextBlock { Text = Localization.Text("Completa la ficha y vincula los equipos del contacto."), TextWrapping = TextWrapping.Wrap, Style = Resource<Style>("MutedTextStyle") });
        content.Children.Add(error); content.Children.Add(Card(details)); content.Children.Add(Card(linked)); content.Children.Add(notes);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot, RequestedTheme = theme,
            Title = existing is null ? Localization.Text("Nuevo contacto") : Localization.Text("Editar contacto"),
            PrimaryButtonText = Localization.Text("Guardar"), CloseButtonText = Localization.Text("Cancelar"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = Math.Max(200, Math.Min(620, xamlRoot.Size.Height - 220)) }
        };
        dialog.Resources["ContentDialogMaxWidth"] = 600d;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            error.IsOpen = false;
            if (string.IsNullOrWhiteSpace(name.Text)) error.Message = Localization.Text("Introduce un nombre para el contacto.");
            else if (!IsValidEmail(email.Text))
                error.Message = Localization.Text("Revisa el correo electrónico.");
            else return;
            args.Cancel = true; error.IsOpen = true;
            error.DispatcherQueue.TryEnqueue(() => error.StartBringIntoView());
        };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            return new Contact(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), notes.Text.Trim(),
                existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
                deviceList.SelectedItems.Cast<ListViewItem>().Select(x => (string)x.Tag).ToArray(),
                company.Text.Trim(), email.Text.Trim(), phone.Text.Trim(), favorite.IsChecked == true);
        }
        finally { picker.Flyout.Hide(); }
    }

    private static TextBox Field(string id, string header, string? value, int maxLength)
    {
        var box = new TextBox { Header = string.IsNullOrEmpty(header) ? null : header, Text = value ?? "", MaxLength = maxLength,
            HeaderTemplate = Resource<DataTemplate>("WrappedLabelTemplate"), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    private static T Resource<T>(string key) => (T)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static Border Card(UIElement child) => new() { Style = Resource<Style>("CardStyle"), Padding = new Thickness(12), Child = child };
    private static bool IsValidEmail(string text)
    {
        var address = text.Trim();
        return address.Length == 0 || (MailAddress.TryCreate(address, out var parsed) &&
            string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase));
    }
}
