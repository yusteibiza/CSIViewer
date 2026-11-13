using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Protocol;
using CSIViewer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

#if CSIVIEWER_HOST
namespace CSIViewer.Host.Views;
#else
namespace CSIViewer.Views;
#endif

public sealed partial class FileTransferPage
{
    private string _localDirectory = "";
    private string? _localParent;
    private string _remoteDirectory = "";
    private string? _remoteParent;
    private string? _remoteDestination;
    private string? _browserSessionId;
    private int _localOffset, _remoteOffset, _localRevision, _remoteRevision;
    private bool _localBusy, _remoteBusy, _sendingSelection;
    private bool _localHasMore, _remoteHasMore;
    private int _browserRefreshPending;
    private int _pullCompletionRevision;

    private void BrowserTransfer_Finished(object? sender, FileBrowserTransferResult result)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded || result.SessionId != _browserSessionId) return;
            _pullCompletionRevision++;
            BrowserMessage(result.Error ?? global::CSIViewer.Core.Localization.Text("Recepción completada y verificada."),
                result.Error is null ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            Interlocked.Exchange(ref _browserRefreshPending, 1);
            RefreshBrowserAfterTransfers();
        });
    }

    private void RefreshBrowserAfterTransfers()
    {
        if (Interlocked.Exchange(ref _browserRefreshPending, 0) == 0 || !_isLoaded) return;
        if (!_localBusy) _ = NavigateLocalAsync(_localDirectory, _localOffset);
        if (!_remoteBusy && _browserSessionId is not null) _ = NavigateRemoteAsync(_remoteDirectory, _remoteOffset);
    }

    private void InitializeBrowser()
    {
        if (LocalFilesList.ItemsSource is null)
            _ = NavigateLocalAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        SynchronizeBrowserSession();
    }

    private void SynchronizeBrowserSession()
    {
        var id = _state.IsSessionActive ? _state.ActiveSessionId : null;
        if (_browserSessionId != id)
        {
            _browserSessionId = id;
            _remoteRevision++;
            _remoteBusy = false;
            _remoteDirectory = "";
            _remoteParent = null;
            _remoteDestination = null;
            _remoteOffset = 0;
            _remoteHasMore = false;
            RemotePathBox.Text = "";
            RemoteFilesList.ItemsSource = null;
            RemoteCountText.Text = "";
        }
        if (!SessionPermissionPolicy.CanTransferFiles(_state.ActivePermissions))
        {
            _remoteRevision++;
            _remoteDestination = null;
            RemoteFilesList.ItemsSource = null;
            _remoteBusy = false;
        }
        RemoteBrowserTitle.Text = global::CSIViewer.Core.Localization.Text("Equipo remoto · ") + (_state.RemoteDeviceName ?? global::CSIViewer.Core.Localization.Text("Sin conexión"));
        RefreshBrowserButtons();
        if (_isLoaded && id is not null && RemoteFilesList.ItemsSource is null && !_remoteBusy &&
            SessionPermissionPolicy.CanTransferFiles(_state.ActivePermissions)) _ = NavigateRemoteAsync("");
    }

    private void RefreshBrowserButtons()
    {
        var active = _browserSessionId is not null && _state.IsSessionActive;
        var canSend = active && !_remoteBusy && !_sendingSelection && _remoteDestination is not null &&
            SessionPermissionPolicy.CanSendFiles(_state.IsController, _state.ActivePermissions);
        SendFilesButton.IsEnabled = canSend;
        SendFolderButton.IsEnabled = canSend;
        SendSelectionButton.IsEnabled = canSend && !_localBusy && _localDirectory.Length > 0 && LocalFilesList.SelectedItems.Count > 0;
        ReceiveSelectionButton.IsEnabled = active && !_sendingSelection && !_remoteBusy && !_localBusy &&
            _localDirectory.Length > 0 && _remoteDirectory.Length > 0 && RemoteFilesList.SelectedItems.Count > 0 &&
            SessionPermissionPolicy.CanReceiveFiles(_state.IsController, _state.ActivePermissions);
        RemoteFilesList.IsEnabled = active && !_remoteBusy;
        LocalFilesList.IsEnabled = !_localBusy;
        LocalPreviousButton.IsEnabled = !_localBusy && _localOffset > 0;
        LocalNextButton.IsEnabled = !_localBusy && _localHasMore;
        RemotePreviousButton.IsEnabled = active && !_remoteBusy && _remoteOffset > 0;
        RemoteNextButton.IsEnabled = active && !_remoteBusy && _remoteHasMore;
    }

    private void BrowserMessage(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        TransferStatusBar.Message = message;
        TransferStatusBar.Severity = severity;
        TransferStatusBar.IsOpen = true;
    }

    private async Task NavigateLocalAsync(string path, int offset = 0)
    {
        var revision = ++_localRevision;
        _localBusy = true;
        RefreshBrowserButtons();
        try
        {
            var result = await Task.Run(() => FileBrowser.Browse(path, offset));
            if (revision != _localRevision || !_isLoaded) return;
            _localDirectory = result.Path;
            _localParent = result.ParentPath;
            _localOffset = offset;
            _localHasMore = result.HasMore;
            LocalPathBox.Text = result.Path;
            LocalFilesList.ItemsSource = result.Entries;
            LocalCountText.Text = $"{offset + result.Entries.Length} elementos";
        }
        catch (Exception ex) { if (_isLoaded && revision == _localRevision) BrowserMessage(ex.Message); }
        finally
        {
            if (revision == _localRevision) { _localBusy = false; RefreshBrowserButtons(); }
        }
    }

    private async Task NavigateRemoteAsync(string path, int offset = 0)
    {
        var sessionId = _browserSessionId;
        if (sessionId is null || _remoteBusy) return;
        var revision = ++_remoteRevision;
        _remoteBusy = true;
        RefreshBrowserButtons();
        try
        {
            var result = await _client.BrowseRemoteFilesAsync(sessionId, path, offset);
            if (!_isLoaded || revision != _remoteRevision || sessionId != _browserSessionId) return;
            if (result.Error is not null) throw new IOException(result.Error);
            _remoteDirectory = result.Path;
            _remoteParent = result.ParentPath;
            _remoteOffset = offset;
            _remoteHasMore = result.HasMore;
            _remoteDestination = result.DestinationToken;
            RemotePathBox.Text = result.Path;
            RemoteFilesList.ItemsSource = result.Entries;
            RemoteCountText.Text = $"{offset + result.Entries.Length} elementos";
        }
        catch (Exception ex) { if (_isLoaded && revision == _remoteRevision) BrowserMessage(ex.Message); }
        finally
        {
            if (revision == _remoteRevision) { _remoteBusy = false; RefreshBrowserButtons(); }
        }
    }

    private void BrowserSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_state is not null) RefreshBrowserButtons();
    }
    private void LocalPath_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = NavigateLocalAsync(LocalPathBox.Text); }
    }
    private void RemotePath_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = NavigateRemoteAsync(RemotePathBox.Text); }
    }
    private void LocalRoots_Click(object sender, RoutedEventArgs e) => _ = NavigateLocalAsync("");
    private void LocalUp_Click(object sender, RoutedEventArgs e) => _ = NavigateLocalAsync(_localParent ?? "");
    private void LocalGo_Click(object sender, RoutedEventArgs e) => _ = NavigateLocalAsync(LocalPathBox.Text);
    private void LocalRefresh_Click(object sender, RoutedEventArgs e) => _ = NavigateLocalAsync(_localDirectory, _localOffset);
    private void RemoteRoots_Click(object sender, RoutedEventArgs e) => _ = NavigateRemoteAsync("");
    private void RemoteUp_Click(object sender, RoutedEventArgs e) => _ = NavigateRemoteAsync(_remoteParent ?? "");
    private void RemoteGo_Click(object sender, RoutedEventArgs e) => _ = NavigateRemoteAsync(RemotePathBox.Text);
    private void RemoteRefresh_Click(object sender, RoutedEventArgs e) => _ = NavigateRemoteAsync(_remoteDirectory, _remoteOffset);
    private void LocalPrevious_Click(object sender, RoutedEventArgs e) => _ = NavigateLocalAsync(_localDirectory, Math.Max(0, _localOffset - FileBrowser.PageSize));
    private void LocalNext_Click(object sender, RoutedEventArgs e) => _ = NavigateLocalAsync(_localDirectory, _localOffset + FileBrowser.PageSize);
    private void RemotePrevious_Click(object sender, RoutedEventArgs e) => _ = NavigateRemoteAsync(_remoteDirectory, Math.Max(0, _remoteOffset - FileBrowser.PageSize));
    private void RemoteNext_Click(object sender, RoutedEventArgs e) => _ = NavigateRemoteAsync(_remoteDirectory, _remoteOffset + FileBrowser.PageSize);
    private void LocalFiles_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LocalFilesList.SelectedItem is FileBrowserEntry { IsDirectory: true } entry) _ = NavigateLocalAsync(entry.FullPath);
    }
    private void RemoteFiles_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RemoteFilesList.SelectedItem is FileBrowserEntry { IsDirectory: true } entry) _ = NavigateRemoteAsync(entry.FullPath);
    }
    private async void SendSelection_Click(object sender, RoutedEventArgs e)
        => await SendPathsAsync(LocalFilesList.SelectedItems.Cast<FileBrowserEntry>().Select(x => x.FullPath).ToArray());

    private async void ReceiveSelection_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = _browserSessionId;
        if (sessionId is null || _sendingSelection) return;
        var paths = RemoteFilesList.SelectedItems.Cast<FileBrowserEntry>().Select(x => x.FullPath).ToArray();
        var completionRevision = _pullCompletionRevision;
        _sendingSelection = true;
        RefreshBrowserButtons();
        try
        {
            await _client.RequestRemoteFilesAsync(sessionId, paths, _localDirectory);
            if (_isLoaded && sessionId == _browserSessionId && completionRevision == _pullCompletionRevision)
                BrowserMessage(global::CSIViewer.Core.Localization.Text("Recepción solicitada. Consulta el progreso inferior."), InfoBarSeverity.Informational);
        }
        catch (Exception ex) { if (_isLoaded && sessionId == _browserSessionId) BrowserMessage(ex.Message); }
        finally { _sendingSelection = false; RefreshBrowserButtons(); }
    }
}
