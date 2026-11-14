using System.Collections.ObjectModel;
using CSIViewer.Core;
using CSIViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CSIViewer.Presentation;

public sealed record ChatDeviceChoice(string DeviceId, string DisplayName)
{
    public override string ToString() => DisplayName;
}
public sealed record ChatViewControls(ComboBox Devices, ListView Messages, InfoBar Status,
    TextBox Message, Button Send, Button Emoji, ComboBox EmojiCategories, GridView Emojis,
    Button Delete, Button Export);

/// <summary>Shared presentation logic, compiled independently into viewer and host.</summary>
public sealed class ChatViewController
{
    private readonly AppState _state;
    private readonly RemoteClient _client;
    private readonly ChatViewControls _view;
    private readonly Func<ChatMessage, object> _row;
    private readonly Func<ElementTheme> _theme;
    private readonly Func<IntPtr> _window;
    private readonly bool _sessionOnly;
    private readonly ObservableCollection<object> _rows = [];
    private ChatMessage[] _visibleMessages = [];
    private readonly HashSet<string> _archivedPeers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);
    private string? _selectedPeer;
    private bool _changingChoices;
    private bool _busy;
    private bool _sending;
    private bool _loading;
    private int _refreshQueued;
    private int _loadVersion;
    private CancellationTokenSource? _loadCts;
    private string? _notice;
    private InfoBarSeverity _noticeSeverity;

    public ChatViewController(AppState state, RemoteClient client, ChatViewControls view,
        Func<ChatMessage, object> row, Func<ElementTheme> theme, Func<IntPtr> window, bool sessionOnly = false)
    {
        _state = state; _client = client; _view = view; _row = row; _theme = theme; _window = window;
        _sessionOnly = sessionOnly;
        view.Messages.ItemsSource = _rows;
        view.Devices.DisplayMemberPath = nameof(ChatDeviceChoice.DisplayName);
        view.EmojiCategories.DisplayMemberPath = nameof(EmojiCategory.Name);
        RefreshEmojiCategories();
        state.ChatMessages.CollectionChanged += (_, _) => QueueRefresh();
        state.KnownDevices.CollectionChanged += (_, _) => QueueRefresh();
        state.SessionChanged += (_, _) => QueueRefresh();
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.IsSessionActive) or nameof(AppState.ActivePermissions) or nameof(AppState.RemoteDeviceId)) QueueRefresh();
        };
        client.ChatMessageReceived += (_, _) => QueueRefresh();
        Localization.Changed += (_, _) => view.Devices.DispatcherQueue.TryEnqueue(() =>
        {
            RefreshEmojiCategories(); RebuildRows(force: true); RefreshState();
        });
        RefreshState();
    }

    public async Task LoadAsync()
    {
        try
        {
            foreach (var peer in await _client.GetChatPeerDeviceIdsAsync()) _archivedPeers.Add(peer);
            RefreshChoices();
            await LoadSelectedAsync();
        }
        catch (Exception) { Notice(Localization.Text("No se pudo cargar el historial de chat."), InfoBarSeverity.Error); }
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        if (!_view.Devices.DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            var changed = RefreshChoices();
            RebuildRows(); RefreshState();
            if (changed) _ = LoadSelectedAsync();
        })) Interlocked.Exchange(ref _refreshQueued, 0);
    }

    private bool RefreshChoices()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string? id, string? alias = null)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "—") return;
            if (!names.ContainsKey(id) || !string.IsNullOrWhiteSpace(alias))
                names[id] = string.IsNullOrWhiteSpace(alias) || alias == id ? id : $"{alias} · {id}";
        }
        if (!_sessionOnly)
        {
            foreach (var id in _archivedPeers) Add(id);
            foreach (var message in _state.ChatMessages) Add(message.PeerDeviceId);
            foreach (var device in _state.KnownDevices) Add(device.DeviceId, device.Alias);
            Add(_selectedPeer);
        }
        if (_state.IsSessionActive) Add(_state.RemoteDeviceId, _state.RemoteDeviceName);
        var choices = names.Select(x => new ChatDeviceChoice(x.Key, x.Value))
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.DeviceId, StringComparer.Ordinal).ToArray();
        var selected = choices.FirstOrDefault(x => x.DeviceId == _selectedPeer)
            ?? choices.FirstOrDefault(x => _state.IsSessionActive && x.DeviceId == _state.RemoteDeviceId)
            ?? choices.FirstOrDefault();
        var changed = selected?.DeviceId != _selectedPeer;
        if (changed && _selectedPeer is { } old) _drafts[old] = _view.Message.Text;
        _selectedPeer = selected?.DeviceId;
        if (_sessionOnly && changed) { _drafts.Clear(); _view.Message.Text = ""; _notice = null; }
        _changingChoices = true;
        try { _view.Devices.ItemsSource = choices; _view.Devices.SelectedItem = selected; }
        finally { _changingChoices = false; }
        if (changed) _view.Message.Text = _selectedPeer is { } id ? _drafts.GetValueOrDefault(id, "") : "";
        return changed;
    }

    public async Task SelectDeviceAsync()
    {
        if (_changingChoices) return;
        var selected = (_view.Devices.SelectedItem as ChatDeviceChoice)?.DeviceId;
        if (selected == _selectedPeer) return;
        if (_selectedPeer is { } old) _drafts[old] = _view.Message.Text;
        _selectedPeer = selected;
        _view.Message.Text = selected is null ? "" : _drafts.GetValueOrDefault(selected, "");
        _notice = null;
        RebuildRows();
        await LoadSelectedAsync();
    }

    private async Task LoadSelectedAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var version = ++_loadVersion;
        var peer = _selectedPeer;
        _loading = peer is not null;
        RefreshState();
        try
        {
            if (!_sessionOnly && peer is not null) await _client.LoadChatHistoryAsync(peer, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version == _loadVersion) Notice(Localization.Text("No se pudo cargar el historial de chat."), InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts)) _loadCts = null;
            cts.Dispose();
            if (version == _loadVersion) { _loading = false; RebuildRows(); RefreshState(); }
        }
    }

    private void RebuildRows(bool force = false)
    {
        var messages = _state.ChatMessages.Where(x => x.PeerDeviceId == _selectedPeer)
            .OrderBy(x => x.SentUtc).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        if (!force && messages.SequenceEqual(_visibleMessages)) return;
        _visibleMessages = messages;
        _rows.Clear();
        foreach (var message in messages) _rows.Add(_row(message));
        if (_rows.Count > 0) _view.Messages.ScrollIntoView(_rows[^1]);
    }

    private void RefreshState()
    {
        var canSend = _selectedPeer is { } peer && _client.CanChatWithDevice(peer);
        _view.Devices.IsEnabled = !_busy;
        _view.Message.IsEnabled = _view.Emoji.IsEnabled = canSend && !_busy;
        _view.Send.IsEnabled = canSend && !_busy && !_sending;
        _view.Delete.IsEnabled = _view.Export.IsEnabled = !_sessionOnly && _selectedPeer is not null && _rows.Count > 0 && !_busy && !_sending && !_loading;
        _view.Status.IsOpen = _notice is not null || _loading || !canSend;
        _view.Status.Severity = _notice is not null ? _noticeSeverity : _loading ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;
        _view.Status.Message = _notice is not null ? Localization.Text(_notice) : (_loading ? Localization.Text("Cargando historial…")
            : Localization.Text("Inicia una sesión con este dispositivo y permiso de chat para enviar mensajes."));
    }

    private void Notice(string message, InfoBarSeverity severity)
    {
        _notice = Localization.Source(message); _noticeSeverity = severity; RefreshState();
    }

    public async Task SendAsync()
    {
        var peer = _selectedPeer;
        var text = _view.Message.Text;
        if (_busy || _sending || peer is null || string.IsNullOrWhiteSpace(text)) return;
        _sending = true; _notice = null; RefreshState();
        try
        {
            var message = await _client.SendChatToDeviceAsync(peer, text);
            if (message is null || message.DeliveryState == ChatDeliveryState.Failed)
                Notice(Localization.Text("No se pudo enviar el mensaje. Comprueba que la sesión tenga permiso de chat."), InfoBarSeverity.Warning);
            else
            {
                if (_selectedPeer == peer && _view.Message.Text == text) _view.Message.Text = "";
                if (_drafts.GetValueOrDefault(peer) == text) _drafts[peer] = "";
            }
        }
        catch (Exception) { Notice(Localization.Text("No se pudo enviar el mensaje."), InfoBarSeverity.Error); }
        finally { _sending = false; RebuildRows(); RefreshState(); }
    }

    public async Task DeleteSelectedAsync()
    {
        if (_sessionOnly || _busy || _sending || _selectedPeer is not { } peer || _rows.Count == 0) return;
        var name = (_view.Devices.SelectedItem as ChatDeviceChoice)?.DisplayName ?? peer;
        _busy = true; RefreshState();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = _view.Devices.XamlRoot, RequestedTheme = _theme(),
                Title = Localization.Text("Borrar historial de chat"),
                Content = Localization.Format("Se borrará el historial local de {0}. No se borrará en el otro equipo. ¿Continuar?", name),
                PrimaryButtonText = Localization.Text("Borrar"), CloseButtonText = Localization.Text("Cancelar"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await _client.DeleteChatHistoryAsync(peer);
            _archivedPeers.Remove(peer);
            Notice(Localization.Text("Historial de chat borrado."), InfoBarSeverity.Success);
        }
        catch (Exception) { Notice(Localization.Text("No se pudo borrar el historial de chat."), InfoBarSeverity.Error); }
        finally { _busy = false; RebuildRows(); RefreshState(); }
    }

    public async Task ExportSelectedAsync()
    {
        if (_sessionOnly || _busy || _sending || _selectedPeer is not { } peer || _rows.Count == 0) return;
        var name = (_view.Devices.SelectedItem as ChatDeviceChoice)?.DisplayName ?? peer;
        _busy = true; RefreshState();
        try
        {
            var history = await _client.GetChatHistoryAsync(peer);
            var messages = history.Concat(_state.ChatMessages.Where(x => x.PeerDeviceId == peer))
                .GroupBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Last()).ToArray();
            var text = ChatTranscript.Format(peer, name, messages);
            var safeId = string.Concat(peer.Where(char.IsAsciiDigit));
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"CSIViewer-chat-{safeId}-{DateTime.Now:yyyyMMdd-HHmmss}",
                DefaultFileExtension = ".txt"
            };
            picker.FileTypeChoices.Add(Localization.Text("Archivo de texto"), new List<string> { ".txt" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _window());
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, text, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            Notice(Localization.Text("Chat exportado."), InfoBarSeverity.Success);
        }
        catch (Exception) { Notice(Localization.Text("No se pudo exportar el chat."), InfoBarSeverity.Error); }
        finally { _busy = false; RefreshState(); }
    }

    private void RefreshEmojiCategories()
    {
        var key = (_view.EmojiCategories.SelectedItem as EmojiCategory)?.Key;
        _view.EmojiCategories.ItemsSource = null;
        _view.EmojiCategories.ItemsSource = EmojiCatalog.Categories;
        _view.EmojiCategories.SelectedItem = EmojiCatalog.Categories.FirstOrDefault(x => x.Key == key) ?? EmojiCatalog.Categories[0];
        SelectEmojiCategory();
    }

    public void SelectEmojiCategory() => _view.Emojis.ItemsSource =
        (_view.EmojiCategories.SelectedItem as EmojiCategory)?.Emojis ?? EmojiCatalog.Categories[0].Emojis;

    public void InsertEmoji(string emoji)
    {
        var box = _view.Message;
        if (!box.IsEnabled || box.Text.Length - box.SelectionLength + emoji.Length > box.MaxLength) return;
        var position = box.SelectionStart;
        box.Text = box.Text.Remove(position, box.SelectionLength).Insert(position, emoji);
        box.SelectionStart = position + emoji.Length;
        box.Focus(FocusState.Programmatic);
    }
}
