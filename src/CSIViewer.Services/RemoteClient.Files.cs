using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Protocol;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private const int FileChunkSize = 1024 * 1024;
    private const int MaxConcurrentFileTransfers = 3;
    private static readonly SemaphoreSlim FileTransferSlots =
        new(MaxConcurrentFileTransfers, MaxConcurrentFileTransfers);

    private readonly ConcurrentDictionary<string, OutgoingFile> _outgoingFiles;
    private readonly ConcurrentDictionary<string, IncomingFile> _incomingFiles;
    private readonly ConcurrentDictionary<string, byte> _resumingTransfers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _activeSendPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TransferItem> _transferItems = new(StringComparer.Ordinal);
    private readonly object _transferGate = new();

    private sealed class OutgoingFile
    {
        public required string TransferId { get; init; }
        public required int FileIndex { get; init; }
        public required string Path { get; init; }
        public required string RelativePath { get; init; }
        public required long Size { get; init; }
        public required string Sha256 { get; init; }
        public RemoteSession? Session { get; init; }
        public TaskCompletionSource<FileAcceptPayload> Accept { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<FileResultPayload> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = new();
        public volatile bool Paused;
        public long ResumeOffset;
    }

    private sealed class IncomingFile
    {
        public required string TransferId { get; init; }
        public required IncomingFileTransfer Storage { get; init; }
        public RemoteSession? Session { get; init; }
        public long LastProgressTicks { get; set; }
        public long LastProgressBytes { get; set; }
    }

    private sealed record FileWork(string Path, string RelativePath, long Size, int FileIndex, string? DestinationToken = null);

    private async Task SendFileControlAsync(
        RemoteSession? session,
        string type,
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            await SendControlAsync(type, payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var timeout = CreateTimeoutToken(TimeSpan.FromSeconds(10), cancellationToken);
        await SendEncryptedAsync(
                session,
                SessionFrameKind.Control,
                ControlJson.Create(type, payload),
                10,
                timeout.Token)
            .ConfigureAwait(false);
    }

    private async Task SendFileChunkAsync(
        RemoteSession? session,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            await SendEncryptedAsync(SessionFrameKind.FileChunk, payload, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SendEncryptedAsync(
                session,
                SessionFrameKind.FileChunk,
                payload,
                10,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<int>> SendFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var targetSession = GetPrimarySession();
        if (targetSession is null)
        {
            return Result<int>.Fail("no_session", "No hay una sesión activa.");
        }

        return await SendPathsToSessionAsync(paths, targetSession, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<int>> SendPathsToSessionAsync(IReadOnlyList<string> paths, RemoteSession targetSession,
        string? destinationToken, CancellationToken cancellationToken)
    {
        if (targetSession.State != ConnectionState.Connected)
            return Result<int>.Fail("no_session", "La sesión ha finalizado.");

        if (!SessionPermissionPolicy.CanSendFiles(
                targetSession.IsController,
                targetSession.Permissions))
        {
            return Result<int>.Fail(
                "no_permission",
                "El equipo remoto no ha concedido permiso para enviar archivos.");
        }

        var files = new List<FileWork>();
        var directories = new List<string>();
        var index = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (destinationToken is not null) FileBrowser.ValidatePath(path);
            if (File.Exists(path))
            {
                files.Add(new FileWork(path, Path.GetFileName(path), new FileInfo(path).Length, index++, destinationToken));
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var root = Path.GetFullPath(path);
            var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(rootName)) return Result<int>.Fail("drive_root", "Selecciona una carpeta, no una unidad completa.");
            directories.Add(rootName);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var directory in Directory.EnumerateDirectories(root, "*", options))
            {
                var relativeDirectory = Path.GetRelativePath(root, directory);
                if (relativeDirectory.Split(Path.DirectorySeparatorChar).Contains(".rdpart", StringComparer.OrdinalIgnoreCase)) continue;
                directories.Add(Path.Combine(rootName, relativeDirectory));
                if (directories.Count > 10000) return Result<int>.Fail("too_many", "Selecciona menos de 10.000 carpetas.");
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                var childRelative = Path.GetRelativePath(root, file);
                if (childRelative.Split(Path.DirectorySeparatorChar).Contains(".rdpart", StringComparer.OrdinalIgnoreCase)) continue;
                var relative = Path.Combine(rootName, childRelative);
                files.Add(new FileWork(file, relative, new FileInfo(file).Length, index++, destinationToken));
                if (files.Count > 10000) return Result<int>.Fail("too_many", "Selecciona menos de 10.000 archivos.");
            }
        }

        if (files.Count == 0 && directories.Count == 0)
        {
            return Result<int>.Fail("empty", "No se seleccionaron archivos.");
        }

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (destinationToken is not null)
                {
                    var requestId = Guid.NewGuid().ToString("N");
                    var pending = new TaskCompletionSource<FileDirectoryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                    targetSession.DirectoryRequests[requestId] = pending;
                    try
                    {
                        await SendFileControlAsync(targetSession, "file.directory", new FileDirectoryPayload(directory, destinationToken, requestId), cancellationToken).ConfigureAwait(false);
                        var response = await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                        if (response.Error is not null) return Result<int>.Fail("directory_failed", response.Error);
                    }
                    finally { targetSession.DirectoryRequests.TryRemove(requestId, out _); }
                    continue;
                }
                await SendFileControlAsync(
                        targetSession,
                        "file.directory",
                        new FileDirectoryPayload(directory, destinationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not announce directory {Directory}", directory);
                if (destinationToken is not null) return Result<int>.Fail("directory_failed", "No se pudo crear la carpeta remota: " + ex.Message);
            }
        }

        var completed = 0;
        await Parallel.ForEachAsync(
                files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentFileTransfers,
                    CancellationToken = cancellationToken
                },
                async (file, token) =>
                {
                    await FileTransferSlots.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (await SendFileCoreAsync(file, targetSession, null, token)
                                .ConfigureAwait(false))
                        {
                            Interlocked.Increment(ref completed);
                        }
                    }
                    finally
                    {
                        FileTransferSlots.Release();
                    }
                })
            .ConfigureAwait(false);

        return files.Count > completed
            ? Result<int>.Fail("incomplete", $"Completados {completed} de {files.Count} archivos. Consulta el progreso para ver rechazos o errores.")
            : Result<int>.Ok(completed);
    }

    private async Task<bool> SendFileCoreAsync(
        FileWork file,
        RemoteSession session,
        TransferItem? existing,
        CancellationToken cancellationToken)
    {
        var pathKey = session.SessionId + "|" + file.DestinationToken + "|" + Path.GetFullPath(file.Path) + "|" + file.RelativePath;
        if (!_activeSendPaths.TryAdd(pathKey, 0))
        {
            _logger.LogInformation(
                "Skipping concurrent duplicate send of {Path}",
                file.RelativePath);
            return false;
        }

        try
        {
            return await SendFileCoreAsyncCore(file, session, existing, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _activeSendPaths.TryRemove(pathKey, out _);
        }
    }

    private async Task<bool> SendFileCoreAsyncCore(
        FileWork file,
        RemoteSession session,
        TransferItem? existing,
        CancellationToken cancellationToken)
    {
        if (session.State != ConnectionState.Connected ||
            !SessionPermissionPolicy.CanSendFiles(session.IsController, session.Permissions)) return false;
        var transferId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var item = existing ?? new TransferItem
        {
            Id = transferId,
            FileName = file.RelativePath,
            Direction = TransferDirection.Send,
            TotalBytes = file.Size,
            State = TransferState.Preparing,
            LocalPath = file.Path
        };

        item.TotalBytes = file.Size;
        item.TransferredBytes = 0;
        item.State = TransferState.Preparing;
        item.BytesPerSecond = 0;
        item.Error = null;
        item.LocalPath = file.Path;
        item.BrowserDestination = file.DestinationToken is null ? null : session.SessionId + "|" + file.DestinationToken;
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        AddOrUpdateTransfer(item);
        _ = PersistTransferAsync(item);

        using var transferScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TransferId"] = transferId,
            ["FileName"] = file.RelativePath,
            ["SessionId"] = session.SessionId
        });

        try
        {
            if (file.DestinationToken is not null) FileBrowser.ValidatePath(file.Path);
            string sha256;
            await using (var hashStream = new FileStream(
                             file.Path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             FileChunkSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                sha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(hashStream, cancellationToken).ConfigureAwait(false));
            }

            var outgoing = new OutgoingFile
            {
                TransferId = transferId,
                FileIndex = file.FileIndex,
                Path = file.Path,
                RelativePath = file.RelativePath,
                Size = file.Size,
                Sha256 = sha256,
                Session = session
            };
            _outgoingFiles[transferId] = outgoing;

            await SendFileControlAsync(
                    session,
                    "file.offer",
                    new FileOfferPayload(
                        transferId,
                        file.FileIndex,
                        Path.GetFileName(file.Path),
                        file.Size,
                        sha256,
                        file.RelativePath,
                        file.DestinationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            var accepted = await Task.WhenAny(
                    outgoing.Accept.Task,
                    Task.Delay(TimeSpan.FromSeconds(20), cancellationToken))
                .ConfigureAwait(false);

            if (accepted != outgoing.Accept.Task)
            {
                FailTransferUnlessInterrupted(transferId, "El receptor no respondió.");
                _outgoingFiles.TryRemove(transferId, out _);
                return false;
            }

            var result = await outgoing.Accept.Task.ConfigureAwait(false);
            if (!result.Accepted)
            {
                FailTransferUnlessInterrupted(
                    transferId,
                    DescribeTransferError(result.Reason) ?? "El receptor rechazó el archivo.");
                _outgoingFiles.TryRemove(transferId, out _);
                return false;
            }

            if (result.ResumeOffset < 0 || result.ResumeOffset > outgoing.Size)
            {
                FailTransferUnlessInterrupted(
                    transferId,
                    "El receptor devolvió un desplazamiento de reanudación no válido.");
                _outgoingFiles.TryRemove(transferId, out _);
                return false;
            }

            outgoing.ResumeOffset = result.ResumeOffset;
            _logger.LogInformation(
                LogEvents.TransferStarted,
                "Transfer started. TransferId={TransferId} FileName={FileName} Bytes={Bytes} ResumeOffset={ResumeOffset}",
                transferId,
                file.RelativePath,
                file.Size,
                result.ResumeOffset);
            await UploadFileAsync(outgoing, item, cancellationToken).ConfigureAwait(false);
            using var completionToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, outgoing.Cancellation.Token);
            var verified = await outgoing.Result.Task.WaitAsync(TimeSpan.FromSeconds(30), completionToken.Token).ConfigureAwait(false);
            ApplyFileResult(verified, item);
            _outgoingFiles.TryRemove(transferId, out _);
            return verified.Success;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            session.State != ConnectionState.Connected)
        {
            _outgoingFiles.TryRemove(transferId, out _);
            if (item.State != TransferState.Interrupted)
            {
                MarkTransferInterrupted(
                    transferId,
                    "Conexión perdida. La transferencia se conserva para reanudar.");
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File transfer failed for {Path}", file.RelativePath);
            FailTransferUnlessInterrupted(transferId, DescribeTransferError(ex));
            _outgoingFiles.TryRemove(transferId, out _);
            return false;
        }
    }

    private async Task UploadFileAsync(
        OutgoingFile outgoing,
        TransferItem item,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            outgoing.Cancellation.Token);
        var token = linked.Token;
        item.State = TransferState.Transferring;
        AddOrUpdateTransfer(item);
        _ = PersistTransferAsync(item);

        await using var stream = new FileStream(
            outgoing.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[FileChunkSize];
        long offset = outgoing.ResumeOffset;
        stream.Seek(offset, SeekOrigin.Begin);
        item.TransferredBytes = offset;
        var lastUpdate = DateTimeOffset.UtcNow;
        var lastBytes = offset;

        while (offset < outgoing.Size)
        {
            token.ThrowIfCancellationRequested();
            while (outgoing.Paused && !token.IsCancellationRequested)
            {
                await Task.Delay(120, token).ConfigureAwait(false);
            }

            if (outgoing.Session is { } activeSession && (activeSession.State != ConnectionState.Connected ||
                !SessionPermissionPolicy.CanSendFiles(activeSession.IsController, activeSession.Permissions)))
                throw new OperationCanceledException("La sesión ha finalizado o se retiró el permiso de archivos.");

            var read = await stream.ReadAsync(
                    buffer.AsMemory(0, FileChunkSize),
                    token)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var payload = FileChunkPayload.Create(
                Guid.Parse(outgoing.TransferId),
                outgoing.FileIndex,
                offset,
                buffer.AsSpan(0, read));
            await SendFileChunkAsync(outgoing.Session, payload, token).ConfigureAwait(false);
            offset += read;

            item.TransferredBytes = offset;
            item.UpdatedUtc = DateTimeOffset.UtcNow;

            var elapsed = item.UpdatedUtc - lastUpdate;
            if (elapsed.TotalMilliseconds > 500)
            {
                item.BytesPerSecond = (offset - lastBytes) / elapsed.TotalSeconds;
                lastBytes = offset;
                lastUpdate = item.UpdatedUtc;
                AddOrUpdateTransfer(item);
            }

            var limit = _state.Settings.UploadLimitKbps;
            if (limit > 0)
            {
                var expectedMs = read / (limit * 1024.0) * 1000.0;
                var elapsedMs = (DateTimeOffset.UtcNow - item.UpdatedUtc).TotalMilliseconds;
                if (expectedMs > elapsedMs)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(expectedMs - elapsedMs), token)
                        .ConfigureAwait(false);
                }
            }
        }

        if (offset < outgoing.Size)
        {
            throw new IOException("El archivo cambió o se acortó durante la transferencia.");
        }

        item.State = TransferState.Verifying;
        item.TransferredBytes = outgoing.Size;
        item.BytesPerSecond = 0;
        AddOrUpdateTransfer(item);
        _ = PersistTransferAsync(item);
    }

    private async Task HandleFileAcceptAsync(
        ControlEnvelope envelope,
        RemoteSession? session = null)
    {
        var accept = ControlJson.Payload<FileAcceptPayload>(envelope);
        if (accept is null ||
            !Guid.TryParse(accept.TransferId, out _) ||
            !MessageValidation.IsValidReason(accept.Reason) ||
            !_outgoingFiles.TryGetValue(accept.TransferId, out var outgoing))
        {
            return;
        }

        if (session is not null && !ReferenceEquals(outgoing.Session, session))
        {
            return;
        }

        outgoing.Accept.TrySetResult(accept);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task HandleFileOfferAsync(
        ControlEnvelope envelope,
        RemoteSession? session = null)
    {
        var offer = ControlJson.Payload<FileOfferPayload>(envelope);
        if (offer is null ||
            !Guid.TryParse(offer.TransferId, out _) ||
            string.IsNullOrWhiteSpace(offer.FileName) ||
            offer.FileName.Length > 255 ||
            !MessageValidation.IsValidTransferPath(offer.RelativePath) ||
            !MessageValidation.IsValidSha256Hex(offer.Sha256) ||
            offer.FileIndex is < 0 or > 100000)
        {
            if (offer is not null && !string.IsNullOrWhiteSpace(offer.TransferId))
            {
                await SendFileControlAsync(
                        session,
                        "file.accept",
                        new FileAcceptPayload(offer.TransferId, false, 0, "invalid_request"))
                    .ConfigureAwait(false);
            }

            return;
        }

        var permissions = session?.Permissions ?? _permissions;
        var localIsController = session?.IsController ?? _isController;
        using var transferScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TransferId"] = offer.TransferId,
            ["FileName"] = string.IsNullOrWhiteSpace(offer.RelativePath)
                ? offer.FileName
                : offer.RelativePath,
            ["SessionId"] = session?.SessionId
        });
        if (!SessionPermissionPolicy.CanReceiveFiles(localIsController, permissions))
        {
            _logger.LogWarning(
                LogEvents.TransferFailed,
                "Rejected file offer without receive permission; session={SessionId} transfer={TransferId}",
                session?.SessionId ?? _activeSessionId ?? "?",
                offer.TransferId);
            await SendFileControlAsync(
                    session,
                    "file.accept",
                    new FileAcceptPayload(offer.TransferId, false, 0, "no_permission"))
                .ConfigureAwait(false);
            return;
        }

        if (offer.Size < 0 || offer.Size > 100L * 1024 * 1024 * 1024)
        {
            await SendFileControlAsync(
                    session,
                    "file.accept",
                    new FileAcceptPayload(offer.TransferId, false, 0, "invalid_size"))
                .ConfigureAwait(false);
            return;
        }

        try
        {
            var root = ResolveFileDestination(session, offer.DestinationToken);
            Directory.CreateDirectory(root);
            var relative = offer.RelativePath;
            var baseFinalPath = PathValidator.GetSafeDestinationPath(root, relative);
            if (offer.DestinationToken is not null) ValidateBrowserWritePath(root, baseFinalPath);
            var destinationExists = File.Exists(baseFinalPath) || Directory.Exists(baseFinalPath);
            var policy = _state.Settings.FileConflictPolicy;

            if (destinationExists && policy == FileConflictPolicy.Skip)
            {
                await SendFileControlAsync(
                        session,
                        "file.accept",
                        new FileAcceptPayload(offer.TransferId, false, 0, "conflict_skipped"))
                    .ConfigureAwait(false);
                return;
            }

            var overwriteConfirmed = false;
            var effectivePolicy = policy;
            var needsPrompt = _state.Settings.AskBeforeReceivingFiles ||
                              (destinationExists && policy == FileConflictPolicy.Overwrite);
            if (needsPrompt)
            {
                var decision = await _consent.RequestFileReceiveAsync(
                        session?.RemoteName ?? _remoteName ?? _remoteId ?? "Equipo remoto",
                        offer.DestinationToken is null ? relative : baseFinalPath,
                        offer.Size,
                        destinationExists,
                        destinationExists && policy == FileConflictPolicy.Overwrite,
                        _lifetimeCts.Token)
                    .ConfigureAwait(false);
                if (decision == FileReceiveDecision.Reject)
                {
                    await SendFileControlAsync(
                            session,
                            "file.accept",
                            new FileAcceptPayload(offer.TransferId, false, 0, "rejected"))
                        .ConfigureAwait(false);
                    return;
                }

                if (decision == FileReceiveDecision.AcceptRename)
                {
                    effectivePolicy = FileConflictPolicy.Rename;
                }
                else if (destinationExists && effectivePolicy == FileConflictPolicy.Overwrite)
                {
                    overwriteConfirmed = true;
                }
            }

            if (session is not null && (session.State != ConnectionState.Connected ||
                !SessionPermissionPolicy.CanReceiveFiles(session.IsController, session.Permissions)))
                throw new IOException("La sesión ha finalizado o se retiró el permiso.");
            if (offer.DestinationToken is not null)
                ValidateBrowserWritePath(ResolveFileDestination(session, offer.DestinationToken), baseFinalPath);

            var opened = await IncomingFileTransfer.OpenAsync(
                    root,
                    offer.TransferId,
                    relative,
                    offer.Size,
                    offer.Sha256,
                    effectivePolicy,
                    overwriteConfirmed)
                .ConfigureAwait(false);

            if (opened.Status == IncomingFileOpenStatus.Skipped)
            {
                await SendFileControlAsync(
                        session,
                        "file.accept",
                        new FileAcceptPayload(offer.TransferId, false, 0, "conflict_skipped"))
                    .ConfigureAwait(false);
                return;
            }

            if (opened.Status == IncomingFileOpenStatus.Rejected || opened.Transfer is null)
            {
                await SendFileControlAsync(
                        session,
                        "file.accept",
                        new FileAcceptPayload(
                            offer.TransferId,
                            false,
                            0,
                            opened.Reason ?? "receiver_error"))
                    .ConfigureAwait(false);
                return;
            }

            var incoming = new IncomingFile
            {
                TransferId = offer.TransferId,
                Storage = opened.Transfer,
                Session = session,
                LastProgressTicks = Environment.TickCount64,
                LastProgressBytes = opened.ResumeOffset
            };
            _incomingFiles[offer.TransferId] = incoming;

            var stale = _transferItems.Values.FirstOrDefault(x =>
                x.Direction == TransferDirection.Receive &&
                x.State == TransferState.Interrupted &&
                string.Equals(x.FileName, relative, StringComparison.OrdinalIgnoreCase));
            if (stale is not null)
            {
                RemoveTransferItem(stale, "Reanudada con una nueva transferencia.");
            }

            AddOrUpdateTransfer(new TransferItem
            {
                Id = offer.TransferId,
                FileName = relative,
                Direction = TransferDirection.Receive,
                TotalBytes = offer.Size,
                TransferredBytes = opened.ResumeOffset,
                State = TransferState.Transferring,
                LocalPath = opened.Transfer.FinalPath
            });

            await SendFileControlAsync(
                    session,
                    "file.accept",
                    new FileAcceptPayload(offer.TransferId, true, opened.ResumeOffset))
                .ConfigureAwait(false);

            if (offer.Size == 0 || opened.ResumeOffset >= offer.Size)
            {
                await FinishIncomingAsync(incoming).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not accept incoming file");
            if (_incomingFiles.TryRemove(offer.TransferId, out var failed))
            {
                try
                {
                    await failed.Storage.AbortAsync(deletePartial: false).ConfigureAwait(false);
                    await failed.Storage.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }

            await SendFileControlAsync(
                    session,
                    "file.accept",
                    new FileAcceptPayload(offer.TransferId, false, 0, "receiver_error"))
                .ConfigureAwait(false);
        }
    }

    private async Task HandleFileChunkAsync(byte[] payload, RemoteSession? session = null)
    {
        if (!FileChunkPayload.TryRead(payload, out var transferId, out _, out var offset, out var data) ||
            data.Length == 0 ||
            data.Length > MessageValidation.MaxFileChunkBytes)
        {
            return;
        }

        var id = transferId.ToString("N");
        if (!_incomingFiles.TryGetValue(id, out var incoming))
        {
            return;
        }

        if (session is not null && !ReferenceEquals(incoming.Session, session))
        {
            return;
        }

        if (session is null && incoming.Session is not null)
        {
            return;
        }

        if (session is not null && !SessionPermissionPolicy.CanReceiveFiles(session.IsController, session.Permissions))
        {
            await AbortIncomingAsync(incoming, deletePartial: false, "Se retiró el permiso de recepción.").ConfigureAwait(false);
            return;
        }

        if (offset != incoming.Storage.ReceivedBytes)
        {
            if (offset < incoming.Storage.ReceivedBytes)
            {
                // Duplicate chunk; the data is already durable.
                return;
            }

            await AbortIncomingAsync(
                    incoming,
                    deletePartial: true,
                    "Se detectó un hueco en la secuencia de bloques.")
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await incoming.Storage.AppendAsync(
                    new ReadOnlyMemory<byte>(
                        payload,
                        FileChunkPayload.HeaderSize,
                        payload.Length - FileChunkPayload.HeaderSize),
                    _lifetimeCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write incoming chunk for {TransferId}", id);
            await AbortIncomingAsync(
                    incoming,
                    deletePartial: false,
                    DescribeTransferError(ex))
                .ConfigureAwait(false);
            return;
        }

        _transferItems.TryGetValue(id, out var item);
        if (item is not null)
        {
            var now = Environment.TickCount64;
            item.TransferredBytes = incoming.Storage.ReceivedBytes;
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            if (now - incoming.LastProgressTicks >= 500 ||
                incoming.Storage.ReceivedBytes >= incoming.Storage.Size)
            {
                var seconds = Math.Max(0.001, (now - incoming.LastProgressTicks) / 1000.0);
                item.BytesPerSecond =
                    (incoming.Storage.ReceivedBytes - incoming.LastProgressBytes) / seconds;
                incoming.LastProgressTicks = now;
                incoming.LastProgressBytes = incoming.Storage.ReceivedBytes;
                AddOrUpdateTransfer(item);
            }
        }

        if (incoming.Storage.ReceivedBytes >= incoming.Storage.Size)
        {
            await FinishIncomingAsync(incoming).ConfigureAwait(false);
        }
    }

    private async Task FinishIncomingAsync(IncomingFile incoming)
    {
        using var transferScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TransferId"] = incoming.TransferId,
            ["FileName"] = Path.GetFileName(incoming.Storage.FinalPath),
            ["SessionId"] = incoming.Session?.SessionId
        });
        _incomingFiles.TryRemove(incoming.TransferId, out _);
        IncomingFileCompleteResult result;
        try
        {
            result = await incoming.Storage.CompleteAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not complete incoming transfer {TransferId}", incoming.TransferId);
            result = new IncomingFileCompleteResult(false, null, "io_error");
        }
        finally
        {
            await incoming.Storage.DisposeAsync().ConfigureAwait(false);
        }

        if (result.Success)
        {
            _transferItems.TryGetValue(incoming.TransferId, out var item);
            if (item is not null)
            {
                item.State = TransferState.Completed;
                item.TransferredBytes = item.TotalBytes;
                item.BytesPerSecond = 0;
                item.LocalPath = result.FinalPath;
                item.Error = null;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
                AddOrUpdateTransfer(item);
                _ = PersistTransferAsync(item);
            }

            await SendFileControlAsync(
                    incoming.Session,
                    "file.result",
                    new FileResultPayload(incoming.TransferId, true, result.FinalPath))
                .ConfigureAwait(false);
            _logger.LogInformation(
                LogEvents.TransferCompleted,
                "Incoming transfer {TransferId} completed. Path={Path} Bytes={Bytes}",
                incoming.TransferId,
                Path.GetFileName(result.FinalPath),
                incoming.Storage.Size);

            if (_state.Settings.NotificationsEnabled)
            {
                _notifications.Show("Archivo recibido", Path.GetFileName(result.FinalPath ?? ""));
            }

            return;
        }

        var error = DescribeTransferError(result.Error) ?? "No se pudo finalizar la recepción.";
        if (string.Equals(result.Error, "integrity", StringComparison.Ordinal))
        {
            error = "La verificación SHA-256 falló. El archivo se ha descartado.";
        }

        FailTransfer(incoming.TransferId, error);
        await SendFileControlAsync(
                incoming.Session,
                "file.result",
                new FileResultPayload(incoming.TransferId, false, null, result.Error))
            .ConfigureAwait(false);
    }

    private Task HandleFileResultAsync(
        ControlEnvelope envelope,
        RemoteSession? session = null)
    {
        var result = ControlJson.Payload<FileResultPayload>(envelope);
        if (result is null ||
            !Guid.TryParse(result.TransferId, out _) ||
            !MessageValidation.IsValidReason(result.Error))
        {
            return Task.CompletedTask;
        }

        if (_outgoingFiles.TryGetValue(result.TransferId, out var outgoing) && ReferenceEquals(outgoing.Session, session))
        {
            outgoing.Result.TrySetResult(result);
        }

        return Task.CompletedTask;
    }

    private void ApplyFileResult(FileResultPayload result, TransferItem item)
    {
        if (item.State is not (TransferState.Cancelled or TransferState.Interrupted))
        {
            item.State = result.Success ? TransferState.Completed : TransferState.Failed;
            item.Error = result.Success
                ? null
                : DescribeTransferError(result.Error) ?? "El receptor no pudo completar la transferencia.";
            item.BytesPerSecond = 0;
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            AddOrUpdateTransfer(item);
            _ = PersistTransferAsync(item);
            if (result.Success)
            {
                _logger.LogInformation(
                    LogEvents.TransferCompleted,
                    "Transfer {TransferId} completed by receiver",
                    result.TransferId);
            }
            else
            {
                _logger.LogWarning(
                    LogEvents.TransferFailed,
                    "Transfer {TransferId} failed at receiver: {Error}",
                    result.TransferId,
                    item.Error);
            }
        }

    }

    private async Task HandleFileCancelAsync(
        ControlEnvelope envelope,
        RemoteSession? session = null)
    {
        var cancel = ControlJson.Payload<FileCancelPayload>(envelope);
        if (cancel is null ||
            !Guid.TryParse(cancel.TransferId, out _) ||
            !MessageValidation.IsValidReason(cancel.Reason))
        {
            return;
        }

        if (_incomingFiles.TryGetValue(cancel.TransferId, out var incoming))
        {
            if (session is not null && !ReferenceEquals(incoming.Session, session))
            {
                return;
            }

            _incomingFiles.TryRemove(cancel.TransferId, out _);
            await incoming.Storage.AbortAsync(deletePartial: true).ConfigureAwait(false);
            await incoming.Storage.DisposeAsync().ConfigureAwait(false);
            SetTransferState(
                cancel.TransferId,
                TransferState.Cancelled,
                DescribeTransferError(cancel.Reason) ?? "El emisor canceló la transferencia.");
            return;
        }

        if (_outgoingFiles.TryGetValue(cancel.TransferId, out var outgoing))
        {
            if (session is not null && !ReferenceEquals(outgoing.Session, session))
            {
                return;
            }

            outgoing.Cancellation.Cancel();
            _outgoingFiles.TryRemove(cancel.TransferId, out _);
            SetTransferState(
                cancel.TransferId,
                TransferState.Cancelled,
                DescribeTransferError(cancel.Reason) ?? "El receptor canceló la transferencia.");
        }
    }

    private Task HandleFilePauseAsync(
        ControlEnvelope envelope,
        RemoteSession? session = null)
    {
        var pause = ControlJson.Payload<FilePausePayload>(envelope);
        if (pause is null ||
            !Guid.TryParse(pause.TransferId, out _) ||
            !_outgoingFiles.TryGetValue(pause.TransferId, out var outgoing))
        {
            return Task.CompletedTask;
        }

        if (session is not null && !ReferenceEquals(outgoing.Session, session))
        {
            return Task.CompletedTask;
        }

        outgoing.Paused = pause.Paused;
        SetTransferState(
            pause.TransferId,
            pause.Paused ? TransferState.Paused : TransferState.Transferring,
            null);
        return Task.CompletedTask;
    }

    private async Task HandleFileDirectoryAsync(
        ControlEnvelope envelope,
        RemoteSession? session = null)
    {
        var directory = ControlJson.Payload<FileDirectoryPayload>(envelope);
        if (directory is null ||
            (directory.RequestId is not null && !Guid.TryParse(directory.RequestId, out _)) ||
            !MessageValidation.IsValidTransferPath(directory.RelativePath))
        {
            return;
        }

        var permissions = session?.Permissions ?? _permissions;
        var localIsController = session?.IsController ?? _isController;
        if (!SessionPermissionPolicy.CanReceiveFiles(localIsController, permissions))
        {
            if (session is not null && directory.RequestId is not null)
                await SendFileControlAsync(session, "file.directory.result", new FileDirectoryResponse(directory.RequestId, "No hay permiso para crear carpetas.")).ConfigureAwait(false);
            return;
        }

        try
        {
            var root = ResolveFileDestination(session, directory.DestinationToken);
            Directory.CreateDirectory(root);
            var path = PathValidator.GetSafeDestinationPath(root, directory.RelativePath);
            if (directory.DestinationToken is not null) ValidateBrowserWritePath(root, path);
            Directory.CreateDirectory(path);
            if (session is not null && directory.RequestId is not null)
                await SendFileControlAsync(session, "file.directory.result", new FileDirectoryResponse(directory.RequestId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not create received directory {Directory}", directory.RelativePath);
            if (session is not null && directory.RequestId is not null)
                await SendFileControlAsync(session, "file.directory.result", new FileDirectoryResponse(directory.RequestId, "No se pudo crear la carpeta de destino.")).ConfigureAwait(false);
        }
    }

    public bool PauseTransfer(string transferId)
    {
        if (_outgoingFiles.TryGetValue(transferId, out var outgoing))
        {
            outgoing.Paused = true;
            SetTransferState(transferId, TransferState.Paused, null);
            return true;
        }

        if (_incomingFiles.TryGetValue(transferId, out var incoming))
        {
            SetTransferState(transferId, TransferState.Paused, null);
            _ = SendPauseAsync(incoming.Session, transferId, true);
            return true;
        }

        return false;
    }

    public bool ResumeTransfer(string transferId)
    {
        if (_outgoingFiles.TryGetValue(transferId, out var outgoing))
        {
            outgoing.Paused = false;
            SetTransferState(transferId, TransferState.Transferring, null);
            return true;
        }

        if (_incomingFiles.TryGetValue(transferId, out var incoming))
        {
            SetTransferState(transferId, TransferState.Transferring, null);
            _ = SendPauseAsync(incoming.Session, transferId, false);
            return true;
        }

        return false;
    }

    public async Task<bool> ResumeInterruptedTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        var session = GetPrimarySession();
        if (session is null ||
            !SessionPermissionPolicy.CanSendFiles(session.IsController, session.Permissions))
        {
            return false;
        }

        _transferItems.TryGetValue(transferId, out var item);
        if (item is null ||
            item.Direction != TransferDirection.Send ||
            item.State != TransferState.Interrupted ||
            string.IsNullOrWhiteSpace(item.LocalPath) ||
            !File.Exists(item.LocalPath))
        {
            return false;
        }

        string? destinationToken = null;
        if (item.BrowserDestination is not null)
        {
            var parts = item.BrowserDestination.Split('|');
            // A token belongs to exactly one authenticated session. Never resume
            // a browser transfer into the default folder or another peer.
            if (parts.Length != 2 || parts[0] != session.SessionId) return false;
            destinationToken = parts[1];
        }

        if (!_resumingTransfers.TryAdd(transferId, 0))
        {
            return false;
        }

        try
        {
            var work = new FileWork(
                item.LocalPath,
                item.FileName,
                new FileInfo(item.LocalPath).Length,
                0,
                destinationToken);
            _logger.LogInformation(
                LogEvents.TransferResumed,
                "Resuming interrupted transfer {TransferId} for {FileName}",
                transferId,
                item.FileName);
            return await SendFileCoreAsync(work, session, item, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _resumingTransfers.TryRemove(transferId, out _);
        }
    }

    public async Task<int> ResumeInterruptedTransfersAsync(
        bool automatic,
        CancellationToken cancellationToken = default)
    {
        if (automatic && !_state.Settings.AutoResumeTransfers)
        {
            return 0;
        }

        var pending = _transferItems.Values
            .Where(x => x.Direction == TransferDirection.Send &&
                        x.State == TransferState.Interrupted &&
                        !string.IsNullOrWhiteSpace(x.LocalPath) &&
                        File.Exists(x.LocalPath))
            .Select(x => x.Id)
            .ToArray();
        if (pending.Length == 0)
        {
            return 0;
        }

        var resumed = 0;
        foreach (var id in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ResumeInterruptedTransferAsync(id, cancellationToken).ConfigureAwait(false))
            {
                resumed++;
            }
        }

        return resumed;
    }

    public async Task<bool> CancelTransferAsync(string transferId)
    {
        var cancelled = false;
        if (_outgoingFiles.TryGetValue(transferId, out var outgoing))
        {
            var session = outgoing.Session;
            outgoing.Cancellation.Cancel();
            _outgoingFiles.TryRemove(transferId, out _);
            cancelled = true;
            try
            {
                await SendFileControlAsync(
                        session,
                        "file.cancel",
                        new FileCancelPayload(transferId, "user"))
                    .ConfigureAwait(false);
            }
            catch
            {
                // The transfer is cancelled locally even if the peer is gone.
            }
        }

        if (_incomingFiles.TryGetValue(transferId, out var incoming))
        {
            _incomingFiles.TryRemove(transferId, out _);
            await incoming.Storage.AbortAsync(deletePartial: true).ConfigureAwait(false);
            await incoming.Storage.DisposeAsync().ConfigureAwait(false);
            try
            {
                await SendFileControlAsync(
                        incoming.Session,
                        "file.cancel",
                        new FileCancelPayload(transferId, "user"))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best effort.
            }

            cancelled = true;
        }

        SetTransferState(transferId, TransferState.Cancelled, null);
        return cancelled;
    }

    private async Task SendPauseAsync(RemoteSession? session, string transferId, bool paused)
    {
        try
        {
            await SendFileControlAsync(
                    session,
                    "file.pause",
                    new FilePausePayload(transferId, paused))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not send transfer pause state for {TransferId}", transferId);
        }
    }

    private async Task AbortIncomingAsync(
        IncomingFile incoming,
        bool deletePartial,
        string message)
    {
        _incomingFiles.TryRemove(incoming.TransferId, out _);
        try
        {
            await incoming.Storage.AbortAsync(deletePartial).ConfigureAwait(false);
        }
        finally
        {
            await incoming.Storage.DisposeAsync().ConfigureAwait(false);
        }

        FailTransferUnlessInterrupted(incoming.TransferId, message);
        try
        {
            await SendFileControlAsync(
                    incoming.Session,
                    "file.cancel",
                    new FileCancelPayload(incoming.TransferId, ClipReason(message)))
                .ConfigureAwait(false);
        }
        catch
        {
            // The sender may already be gone.
        }
    }

    private void SetTransferState(string transferId, TransferState state, string? error)
    {
        _transferItems.TryGetValue(transferId, out var item);
        if (item is null)
        {
            return;
        }

        item.State = state;
        item.Error = error;
        item.BytesPerSecond = 0;
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        AddOrUpdateTransfer(item);
        _ = PersistTransferAsync(item);
    }

    /// <summary>
    /// Removes a finished transfer from the list. Active transfers must be
    /// cancelled first so the UI never hides work that is still running.
    /// </summary>
    public bool DismissTransfer(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!_transferItems.TryGetValue(id, out var item))
        {
            item = _state.Transfers.FirstOrDefault(x => x.Id == id);
            if (item is null)
            {
                return false;
            }
        }

        if (item.IsActive)
        {
            return false;
        }

        _transferItems.TryRemove(id, out _);
        _state.Post(() =>
        {
            lock (_transferGate)
            {
                _state.Transfers.Remove(item);
            }
        });
        TransferUpdated?.Invoke(this, item);
        return true;
    }

    /// <summary>
    /// Removes every finished, cancelled or failed transfer from the list.
    /// Active transfers are preserved.
    /// </summary>
    public int DismissFinishedTransfers()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _transferItems.Values.Where(x => !x.IsActive).ToArray())
        {
            ids.Add(item.Id);
        }

        foreach (var item in _state.Transfers.Where(x => !x.IsActive).ToArray())
        {
            ids.Add(item.Id);
        }

        foreach (var id in ids)
        {
            _transferItems.TryRemove(id, out _);
        }

        _state.Post(() =>
        {
            lock (_transferGate)
            {
                foreach (var item in _state.Transfers
                             .Where(x => !x.IsActive && ids.Contains(x.Id))
                             .ToArray())
                {
                    _state.Transfers.Remove(item);
                }
            }
        });

        return ids.Count;
    }

    private void RemoveTransferItem(TransferItem item, string reason)
    {
        item.State = TransferState.Cancelled;
        item.Error = reason;
        item.BytesPerSecond = 0;
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        _transferItems.TryRemove(item.Id, out _);
        _ = PersistTransferAsync(item);
        _state.Post(() =>
        {
            lock (_transferGate)
            {
                _state.Transfers.Remove(item);
            }
        });
        TransferUpdated?.Invoke(this, item);
    }

    private void AddOrUpdateTransfer(TransferItem item)
    {
        _transferItems[item.Id] = item;
        _state.Post(() =>
        {
            lock (_transferGate)
            {
                var index = -1;
                for (var i = 0; i < _state.Transfers.Count; i++)
                {
                    var current = _state.Transfers[i];
                    if (current is not null && current.Id == item.Id)
                    {
                        index = i;
                        break;
                    }
                }

                if (index >= 0)
                {
                    _state.Transfers[index] = item;
                }
                else
                {
                    _state.Transfers.Insert(0, item);
                }
            }
        });

        TransferUpdated?.Invoke(this, item);
    }

    private void TrackLoadedTransfer(TransferItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            _transferItems[item.Id] = item;
        }
    }

    private void ResetLoadedTransfers()
    {
        _transferItems.Clear();
    }

    private void FailTransferUnlessInterrupted(string transferId, string message)
    {
        _transferItems.TryGetValue(transferId, out var item);
        if (item?.State is TransferState.Interrupted or TransferState.Cancelled)
        {
            return;
        }

        FailTransfer(transferId, message);
    }

    private void FailTransfer(string transferId, string message)
    {
        _transferItems.TryGetValue(transferId, out var item);
        if (item is not null)
        {
            if (item.State is TransferState.Completed or TransferState.Cancelled)
            {
                return;
            }

            item.State = TransferState.Failed;
            item.Error = message;
            item.BytesPerSecond = 0;
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            AddOrUpdateTransfer(item);
            _ = PersistTransferAsync(item);
        }

        _logger.LogWarning(LogEvents.TransferFailed, "Transfer {TransferId} failed: {Message}", transferId, message);
    }

    /// <summary>
    /// Marks a transfer as interrupted by a disconnect while keeping the
    /// partial file and sidecar on disk. A later offer for the same file can
    /// resume from the last durable offset after a new authorization.
    /// </summary>
    private void MarkTransferInterrupted(string transferId, string message)
    {
        _transferItems.TryGetValue(transferId, out var item);
        if (item is null)
        {
            return;
        }

        item.State = TransferState.Interrupted;
        item.Error = message;
        item.BytesPerSecond = 0;
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        AddOrUpdateTransfer(item);
        _ = PersistTransferAsync(item);
        _logger.LogInformation(
            LogEvents.TransferInterrupted,
            "Transfer {TransferId} interrupted at {Bytes}/{Total} bytes; partial data preserved",
            transferId,
            item.TransferredBytes,
            item.TotalBytes);
    }

    private async Task PersistTransferAsync(TransferItem item)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _transferRepository.UpsertAsync(item, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist transfer {TransferId}", item.Id);
        }
    }

    private static string ClipReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "io_error";
        }

        return reason.Length <= MessageValidation.MaxReasonLength
            ? reason
            : reason[..MessageValidation.MaxReasonLength];
    }

    private static string? DescribeTransferError(string? code) => code switch
    {
        null or "" => null,
        "invalid_request" => "La oferta de archivo no es válida.",
        "invalid_size" => "El tamaño del archivo no es válido.",
        "invalid_path" => "La ruta del archivo no es segura.",
        "no_permission" => "No hay permiso para transferir archivos.",
        "conflict_skipped" => "El archivo ya existe y la política es omitir.",
        "overwrite_required" => "Es necesario confirmar la sobrescritura.",
        "rejected" => "El receptor rechazó el archivo.",
        "busy" => "El archivo ya se está recibiendo en otra transferencia.",
        "no_space" => "No hay espacio suficiente en la carpeta de descargas.",
        "access_denied" => "Windows denegó el acceso a la carpeta de descargas.",
        "receiver_error" => "El receptor no pudo preparar el archivo.",
        "io_error" => "Error de entrada/salida al escribir el archivo.",
        "integrity" => "La verificación SHA-256 falló.",
        "user" => "Transferencia cancelada por el usuario.",
        _ => code
    };

    private static string DescribeTransferError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Windows denegó el acceso a la carpeta de descargas.",
        PathTooLongException => "La ruta del archivo es demasiado larga.",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) ||
                           io.Message.Contains("espacio", StringComparison.OrdinalIgnoreCase) =>
            "No hay espacio suficiente en la carpeta de descargas.",
        IOException => "Error de entrada/salida al transferir el archivo.",
        _ => "La transferencia falló: " + exception.Message
    };
}
