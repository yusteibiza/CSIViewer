using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CSIViewer.Core;
using CSIViewer.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CSIViewer.Views;

public sealed partial class FileTransferPage : Page
{
    private readonly AppState _state;
    private readonly RemoteClient _client;
    private readonly DispatcherQueueTimer _refreshTimer;
    private int _refreshScheduled;
    private bool _isLoaded;

    public FileTransferPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _state = App.Services.GetRequiredService<AppState>();
        _client = App.Services.GetRequiredService<RemoteClient>();
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(250);
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += (_, _) =>
        {
            Interlocked.Exchange(ref _refreshScheduled, 0);
            if (_isLoaded)
            {
                RefreshList();
                RefreshBrowserAfterTransfers();
            }
        };
        TransfersList.ItemsSource = _state.Transfers;
        _client.TransferUpdated += Client_TransferUpdated;
        _client.FileBrowserTransferFinished += BrowserTransfer_Finished;
        _state.SessionChanged += State_SessionChanged;
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            RefreshState();
            InitializeBrowser();
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            _localRevision++;
            _remoteRevision++;
            _localBusy = _remoteBusy = false;
            _refreshTimer.Stop();
            Interlocked.Exchange(ref _refreshScheduled, 0);
        };
    }

    private void State_SessionChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshState);
    }

    private void Client_TransferUpdated(object? sender, TransferItem item)
    {
        if (_isLoaded && item.State == TransferState.Completed) Interlocked.Exchange(ref _browserRefreshPending, 1);
        if (!_isLoaded || Interlocked.Exchange(ref _refreshScheduled, 1) != 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded)
            {
                Interlocked.Exchange(ref _refreshScheduled, 0);
                return;
            }

            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void RefreshList()
    {
        TransfersList.ItemsSource = null;
        TransfersList.ItemsSource = _state.Transfers;
        UpdateSummary();
    }

    private void RefreshState()
    {
        SynchronizeBrowserSession();
        TransferStatusBar.IsOpen = !_state.IsSessionActive;
        if (!_state.IsSessionActive)
        {
            TransferStatusBar.Severity = InfoBarSeverity.Warning;
            TransferStatusBar.Message = global::CSIViewer.Core.Localization.Text("Inicia una sesión con permiso de archivos para transferir.");
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var active = _state.Transfers.Where(x => x.IsActive).ToArray();
        var interrupted = _state.Transfers.Count(x => x.State == TransferState.Interrupted);
        var total = active.Sum(x => Math.Max(0, x.TotalBytes));
        var transferred = active.Sum(x => Math.Clamp(x.TransferredBytes, 0, Math.Max(0, x.TotalBytes)));
        var speed = active.Sum(x => Math.Max(0, x.BytesPerSecond));

        if (active.Length == 0)
        {
            SummaryText.Text = interrupted > 0
                ? $"{interrupted} transferencia(s) interrumpida(s)"
                : global::CSIViewer.Core.Localization.Text("Sin transferencias activas");
        }
        else
        {
            SummaryText.Text =
                $"{active.Length} activa(s) · {FormatBytes(transferred)} / {FormatBytes(total)}";
        }

        SummaryDetailText.Text = speed > 0
            ? $"{FormatSpeed(speed)} · {interrupted} interrumpida(s)"
            : $"{interrupted} interrumpida(s)";
        TotalProgressBar.Value = total > 0
            ? Math.Clamp((double)transferred / total, 0, 1)
            : 0;

        PauseAllButton.IsEnabled = active.Any(x => x.CanPause);
        ResumeAllButton.IsEnabled = interrupted > 0;
        ClearFinishedButton.IsEnabled = _state.Transfers.Any(x => !x.IsActive);
    }

    private async void SendFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        await SendPathsAsync(files.Select(x => x.Path).ToArray());
    }

    private async void SendFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await SendPathsAsync([folder.Path]);
    }

    private async Task SendPathsAsync(IReadOnlyList<string> paths)
    {
        if (_sendingSelection || _browserSessionId is null || _remoteDestination is null) return;
        var sessionId = _browserSessionId;
        var destination = _remoteDestination;
        _sendingSelection = true;
        RefreshBrowserButtons();
        SendFilesButton.IsEnabled = false;
        SendFolderButton.IsEnabled = false;
        TransferStatusBar.Severity = InfoBarSeverity.Informational;
        TransferStatusBar.Message = global::CSIViewer.Core.Localization.Text("Preparando transferencia…");
        TransferStatusBar.IsOpen = true;

        try
        {
            var result = await _client.SendFilesToRemoteFolderAsync(sessionId, paths, destination);
            if (!_isLoaded || sessionId != _browserSessionId) return;
            TransferStatusBar.Severity = result.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            TransferStatusBar.Message = result.IsSuccess
                ? global::CSIViewer.Core.Localization.Format("Transferencia completada: {0} archivo(s).",result.Value)
                : result.Error?.Message ?? global::CSIViewer.Core.Localization.Text("No se pudo transferir.");
        }
        catch (Exception ex)
        {
            TransferStatusBar.Severity = InfoBarSeverity.Error;
            TransferStatusBar.Message = global::CSIViewer.Core.Localization.Text("La transferencia falló: ") + ex.Message;
        }
        finally
        {
            _sendingSelection = false;
            RefreshState();
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TransferItem item })
        {
            _client.PauseTransfer(item.Id);
        }
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TransferItem item })
        {
            return;
        }

        if (_client.ResumeTransfer(item.Id))
        {
            return;
        }

        var resumed = await _client.ResumeInterruptedTransferAsync(item.Id);
        if (!resumed)
        {
            TransferStatusBar.Severity = InfoBarSeverity.Warning;
            TransferStatusBar.Message =
                global::CSIViewer.Core.Localization.Text("No se pudo reanudar. Si la sesión cambió, vuelve a seleccionar los archivos y la carpeta de destino.");
            TransferStatusBar.IsOpen = true;
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TransferItem item })
        {
            await _client.CancelTransferAsync(item.Id);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TransferItem item })
        {
            return;
        }

        if (!_client.DismissTransfer(item.Id))
        {
            TransferStatusBar.Severity = InfoBarSeverity.Warning;
            TransferStatusBar.Message = global::CSIViewer.Core.Localization.Text("Cancela la transferencia antes de quitarla de la lista.");
            TransferStatusBar.IsOpen = true;
        }

        UpdateSummary();
    }

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        var removed = _client.DismissFinishedTransfers();
        UpdateSummary();
        TransferStatusBar.Severity = InfoBarSeverity.Informational;
        TransferStatusBar.Message = removed == 0
            ? global::CSIViewer.Core.Localization.Text("No hay transferencias terminadas que quitar.")
            : $"Se quitaron {removed} transferencia(s) terminada(s).";
        TransferStatusBar.IsOpen = true;
    }

    private void PauseAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _state.Transfers.Where(x => x.CanPause).ToArray())
        {
            _client.PauseTransfer(item.Id);
        }

        UpdateSummary();
    }

    private async void ResumeAll_Click(object sender, RoutedEventArgs e)
    {
        var paused = _state.Transfers.Where(x => x.State == TransferState.Paused).ToArray();
        foreach (var item in paused)
        {
            _client.ResumeTransfer(item.Id);
        }

        var resumed = await _client.ResumeInterruptedTransfersAsync(automatic: false);
        if (paused.Length > 0 || resumed > 0)
        {
            TransferStatusBar.Severity = InfoBarSeverity.Success;
            TransferStatusBar.Message =
                $"Reanudadas {paused.Length + resumed} transferencia(s).";
            TransferStatusBar.IsOpen = true;
        }

        UpdateSummary();
    }

    private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_state.Settings.DownloadFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_state.Settings.DownloadFolder}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            TransferStatusBar.Severity = InfoBarSeverity.Error;
            TransferStatusBar.Message = global::CSIViewer.Core.Localization.Text("No se pudo abrir la carpeta de descargas.");
            TransferStatusBar.IsOpen = true;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond switch
        {
            >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.0} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:0.0} KB/s",
            _ => $"{bytesPerSecond:0} B/s"
        };
}
