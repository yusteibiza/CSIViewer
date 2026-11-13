using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Core;
using CSIViewer.Services;
using Windows.System;

namespace CSIViewer.Host.Views;

public sealed class ChatRow
{
    public ChatRow(ChatMessage message) => Message = message;

    public ChatMessage Message { get; }
    public string Text => Message.Text;
    public string MetaText =>
        $"{Message.SentUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {StateText}";
    public Visibility SentVisibility => Message.IsMine ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReceivedVisibility => Message.IsMine ? Visibility.Collapsed : Visibility.Visible;

    private string StateText => Message.DeliveryState switch
    {
        ChatDeliveryState.Sending => "Enviando",
        ChatDeliveryState.Sent => global::CSIViewer.Core.Localization.Text("Enviado"),
        ChatDeliveryState.Received => global::CSIViewer.Core.Localization.Text("Recibido"),
        ChatDeliveryState.Failed => global::CSIViewer.Core.Localization.Text("Fallido"),
        _ => ""
    };
}

public sealed partial class ChatPage : Page
{
    private static readonly string[] Emojis =
    [
        "😀", "😁", "😂", "🤣", "😊", "😍", "😘", "😎",
        "🤔", "😅", "😢", "😭", "😡", "👍", "👎", "👏",
        "🙏", "💪", "👋", "❤️", "💙", "🎉", "🔥", "✅"
    ];

    private readonly AppState _state;
    private readonly RemoteClient _client;
    private readonly ObservableCollection<ChatRow> _rows = [];
    private int _rebuildScheduled;

    public ChatPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        ChatList.ItemsSource = _rows;
        _state.ChatMessages.CollectionChanged += (_, _) => ScheduleRebuild();
        EmojiGrid.ItemsSource = Emojis;
        _client.ChatMessageReceived += Client_ChatMessageReceived;
        _client.SessionStarted += Client_SessionStarted;
        Loaded += async (_, _) =>
        {
            RefreshState();
            if (_state.IsSessionActive && !string.IsNullOrWhiteSpace(_state.RemoteDeviceId))
            {
                await _client.LoadChatHistoryAsync(_state.RemoteDeviceId);
            }

            RebuildRows();
        };
    }

    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var message in _state.ChatMessages)
        {
            _rows.Add(new ChatRow(message));
        }

        ScrollToEnd();
    }

    private void ScheduleRebuild()
    {
        if (Interlocked.Exchange(ref _rebuildScheduled, 1) != 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _rebuildScheduled, 0);
            RebuildRows();
        });
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string emoji })
        {
            return;
        }

        var position = MessageBox.SelectionStart;
        MessageBox.Text = MessageBox.Text.Insert(position, emoji);
        MessageBox.SelectionStart = position + emoji.Length;
        MessageBox.Focus(FocusState.Programmatic);
    }

    private async void Client_SessionStarted(object? sender, SessionInfo info)
    {
        await _client.LoadChatHistoryAsync(info.RemoteDeviceId);
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshState();
            ScheduleRebuild();
        });
    }

    private void Client_ChatMessageReceived(object? sender, ChatMessage message)
    {
        ScheduleRebuild();
    }

    private void RefreshState()
    {
        if (_state.IsSessionActive && _state.ActivePermissions.HasFlag(SessionPermissions.Chat))
        {
            ChatStatusBar.IsOpen = false;
            MessageBox.IsEnabled = true;
        }
        else
        {
            ChatStatusBar.Severity = InfoBarSeverity.Warning;
            ChatStatusBar.Message = global::CSIViewer.Core.Localization.Text("Inicia una sesión con permiso de chat para enviar mensajes.");
            ChatStatusBar.IsOpen = true;
            MessageBox.IsEnabled = false;
        }
    }

    private void ScrollToEnd()
    {
        if (_rows.Count > 0)
        {
            ChatList.ScrollIntoView(_rows[^1]);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        await SendAsync();
    }

    private async void MessageBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        var text = MessageBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MessageBox.Text = "";
        var message = await _client.SendChatAsync(text);
        if (message is null)
        {
            ChatStatusBar.Severity = InfoBarSeverity.Warning;
            ChatStatusBar.Message = global::CSIViewer.Core.Localization.Text("No se pudo enviar el mensaje. Comprueba que la sesión tenga permiso de chat.");
            ChatStatusBar.IsOpen = true;
        }
        else
        {
            ScrollToEnd();
        }
    }
}
