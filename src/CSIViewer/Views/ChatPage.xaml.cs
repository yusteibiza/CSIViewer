using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Core;
using CSIViewer.Services;
using CSIViewer.Presentation;
using Windows.System;
using WinRT.Interop;

namespace CSIViewer.Views;

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
        ChatDeliveryState.Sending => Localization.Text("Enviando"),
        ChatDeliveryState.Sent => global::CSIViewer.Core.Localization.Text("Enviado"),
        ChatDeliveryState.Received => global::CSIViewer.Core.Localization.Text("Recibido"),
        ChatDeliveryState.Failed => global::CSIViewer.Core.Localization.Text("Fallido"),
        _ => ""
    };
}

public sealed partial class ChatPage : Page
{
    private readonly ChatViewController? _chat;

    public ChatPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _chat = new ChatViewController(App.Services.GetRequiredService<AppState>(),
            App.Services.GetRequiredService<RemoteClient>(),
            new ChatViewControls(ChatDeviceCombo, ChatList, ChatStatusBar, MessageBox, SendButton,
                EmojiButton, EmojiCategoryCombo, EmojiGrid, DeleteChatButton, ExportChatButton),
            message => new ChatRow(message), () => ThemeService.Current,
            () => MainWindow.Instance is { } window ? WindowNative.GetWindowHandle(window) : IntPtr.Zero);
        Loaded += async (_, _) => await _chat.LoadAsync();
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string emoji }) _chat?.InsertEmoji(emoji);
    }

    private async void ChatDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (_chat is not null) await _chat.SelectDeviceAsync(); }

    private void EmojiCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => _chat?.SelectEmojiCategory();

    private async void DeleteChat_Click(object sender, RoutedEventArgs e)
    { if (_chat is not null) await _chat.DeleteSelectedAsync(); }

    private async void ExportChat_Click(object sender, RoutedEventArgs e)
    { if (_chat is not null) await _chat.ExportSelectedAsync(); }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_chat is not null) await _chat.SendAsync();
    }

    private async void MessageBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (_chat is not null) await _chat.SendAsync();
        }
    }

}
