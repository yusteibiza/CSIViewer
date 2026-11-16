using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSIViewer.Application;

public enum IncomingFileOpenStatus
{
    Accepted,
    Skipped,
    Rejected
}

public sealed record IncomingFileOpenResult(
    IncomingFileOpenStatus Status,
    IncomingFileTransfer? Transfer = null,
    string? Reason = null,
    long ResumeOffset = 0);

public sealed record IncomingFileCompleteResult(bool Success, string? FinalPath, string? Error = null);

/// <summary>
/// Streams a received file to a temporary path, verifies its SHA-256 and only
/// then moves it to its final destination. The class keeps a sidecar next to
/// the partial file so an interrupted transfer can resume from the last
/// durable offset after a reconnection.
/// </summary>
public sealed class IncomingFileTransfer : IAsyncDisposable
{
    public const long FreeSpaceReserveBytes = 64L * 1024 * 1024;
    private const int FlushIntervalBytes = 4 * 1024 * 1024;
    private const int StreamBufferBytes = 1024 * 1024;
    private static readonly ConcurrentDictionary<string, byte> ActiveTempPaths =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _stream;
    private IncrementalHash? _hash;
    private readonly string _activeKey;
    private readonly string _rootDirectory;
    private readonly TransferDirectoryLease _directoryLease;
    private long _lastFlushedBytes;
    private int _activeKeyReleased;
    private bool _completed;
    private volatile bool _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    private IncomingFileTransfer(
        string rootDirectory,
        TransferDirectoryLease directoryLease,
        string transferId,
        string finalPath,
        string tempPath,
        string sidecarPath,
        long size,
        string expectedSha256,
        FileStream stream,
        IncrementalHash hash,
        long receivedBytes,
        bool overwrite)
    {
        _rootDirectory = rootDirectory;
        _directoryLease = directoryLease;
        TransferId = transferId;
        FinalPath = finalPath;
        TempPath = tempPath;
        SidecarPath = sidecarPath;
        Size = size;
        ExpectedSha256 = expectedSha256;
        _stream = stream;
        _hash = hash;
        _activeKey = tempPath;
        ReceivedBytes = receivedBytes;
        _lastFlushedBytes = receivedBytes;
        Overwrite = overwrite;
    }

    public string TransferId { get; }
    public string FinalPath { get; }
    public string TempPath { get; }
    public string SidecarPath { get; }
    public long Size { get; }
    public string ExpectedSha256 { get; }
    public long ReceivedBytes { get; private set; }
    public bool Overwrite { get; }
    public bool IsCompleted => _completed;

    public static async Task<IncomingFileOpenResult> OpenAsync(
        string rootDirectory,
        string transferId,
        string relativePath,
        long size,
        string expectedSha256,
        FileConflictPolicy conflictPolicy,
        bool overwriteConfirmed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(transferId) ||
            size < 0 ||
            expectedSha256 is not { Length: 64 } ||
            !expectedSha256.All(Uri.IsHexDigit))
        {
            return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "invalid_request");
        }

        FileStream? stream = null;
        IncrementalHash? hash = null;
        string? activeTempPath = null;
        var activeRegistered = false;
        TransferDirectoryLease? directoryLease = null;
        var leaseTransferred = false;
        try
        {
            var root = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(root);
            var baseFinalPath = PathValidator.GetSafeDestinationPath(root, relativePath);
            PathValidator.EnsureSafeTransferDestination(root, baseFinalPath);
            var tempDirectory = Path.Combine(root, ".rdpart");
            PathValidator.EnsureNoReparsePoints(root, tempDirectory);
            directoryLease = new TransferDirectoryLease(root);
            directoryLease.SetDestination(baseFinalPath);

            var key = ComputeKey(relativePath, size, expectedSha256);
            var sidecarPath = Path.Combine(tempDirectory, key + ".json");
            if (ActiveTempPaths.ContainsKey(Path.Combine(tempDirectory, key + ".part")))
                return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "busy");
            var partial = await ReadPartialAsync(
                    directoryLease,
                    tempDirectory,
                    sidecarPath,
                    root,
                    relativePath,
                    size,
                    expectedSha256,
                    cancellationToken)
                .ConfigureAwait(false);

            var tempPath = partial?.TempPath ?? Path.Combine(tempDirectory, key + ".part");
            PathValidator.EnsureNoReparsePoints(root, tempPath);
            var resumeOffset = 0L;
            if (partial is not null)
            {
                var length = directoryLease.TemporaryLength(Path.GetFileName(tempPath));
                if (length >= 0 && length <= size)
                {
                    resumeOffset = length;
                }
                else
                {
                    directoryLease.DeleteTemporary(Path.GetFileName(tempPath));
                    directoryLease.DeleteTemporary(Path.GetFileName(partial.SidecarPath!));
                    partial = null;
                }
            }

            string finalPath;
            bool overwrite;
            if (partial is not null)
            {
                // Never trust a persisted path without checking it again.
                finalPath = PathValidator.EnsureInsideRoot(root, partial.FinalPath);
                overwrite = partial.Overwrite;
            }
            else if (directoryLease.DestinationExists)
            {
                switch (conflictPolicy)
                {
                    case FileConflictPolicy.Skip:
                        return new IncomingFileOpenResult(
                            IncomingFileOpenStatus.Skipped,
                            Reason: "conflict_skipped");
                    case FileConflictPolicy.Overwrite:
                        if (!overwriteConfirmed)
                        {
                            return new IncomingFileOpenResult(
                                IncomingFileOpenStatus.Rejected,
                                Reason: "overwrite_required");
                        }

                        finalPath = baseFinalPath;
                        overwrite = true;
                        break;
                    default:
                        finalPath = directoryLease.UniqueDestination();
                        overwrite = false;
                        break;
                }
            }
            else
            {
                finalPath = baseFinalPath;
                overwrite = false;
            }

            if (!HasEnoughFreeSpace(root, Math.Max(0, size - resumeOffset)))
            {
                return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "no_space");
            }

            PathValidator.EnsureSafeTransferDestination(root, finalPath);
            directoryLease.SetDestination(finalPath);

            activeTempPath = tempPath;
            if (!ActiveTempPaths.TryAdd(tempPath, 0))
            {
                return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "busy");
            }

            activeRegistered = true;

            if (partial is not null)
            {
                stream = directoryLease.OpenTemporary(Path.GetFileName(tempPath), resume: true);
                hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                try
                {
                    var buffer = new byte[StreamBufferBytes];
                    int read;
                    long hashed = 0;
                    while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hashed += read;
                        if (hashed > size)
                        {
                            throw new InvalidDataException(global::CSIViewer.Core.Localization.Text("El archivo parcial es mayor que el tamaño declarado."));
                        }

                        hash.AppendData(buffer, 0, read);
                    }

                    resumeOffset = hashed;
                    stream.Seek(0, SeekOrigin.End);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    hash.Dispose();
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            else
            {
                directoryLease.DeleteTemporary(Path.GetFileName(tempPath));
                stream = directoryLease.OpenTemporary(Path.GetFileName(tempPath), resume: false);
                hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                resumeOffset = 0;
            }

            var metadata = new PartialMetadata(
                transferId,
                relativePath,
                finalPath,
                tempPath,
                size,
                expectedSha256,
                overwrite);
            await directoryLease.WriteMetadataAsync(Path.GetFileName(sidecarPath), JsonSerializer.Serialize(metadata), cancellationToken).ConfigureAwait(false);
            directoryLease.SealDestination();

            activeRegistered = false;
            leaseTransferred = true;
            return new IncomingFileOpenResult(
                IncomingFileOpenStatus.Accepted,
                new IncomingFileTransfer(
                    root,
                    directoryLease,
                    transferId,
                    finalPath,
                    tempPath,
                    sidecarPath,
                    size,
                    expectedSha256,
                    stream!,
                    hash!,
                    resumeOffset,
                    overwrite),
                ResumeOffset: resumeOffset);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException)
        {
            return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "access_denied");
        }
        catch (InvalidOperationException)
        {
            return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "invalid_path");
        }
        catch (IOException)
        {
            return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "io_error");
        }
        catch (Exception)
        {
            return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "receiver_error");
        }
        finally
        {
            if (activeRegistered)
            {
                try
                {
                    hash?.Dispose();
                    stream?.Dispose();
                }
                catch
                {
                    // Best effort cleanup.
                }

                if (activeTempPath is not null)
                {
                    ActiveTempPaths.TryRemove(activeTempPath, out _);
                }
            }
            if (!leaseTransferred) directoryLease?.Dispose();
        }
    }

    public async Task AppendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.Length == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfNotWritable();
            if (data.Length > Size - ReceivedBytes)
            {
                throw new InvalidDataException(global::CSIViewer.Core.Localization.Text("El bloque supera el tamaño declarado del archivo."));
            }

            await _stream!.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            _hash!.AppendData(data.Span);
            ReceivedBytes += data.Length;

            if (ReceivedBytes >= Size ||
                ReceivedBytes - _lastFlushedBytes >= FlushIntervalBytes)
            {
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                _lastFlushedBytes = ReceivedBytes;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IncomingFileCompleteResult> CompleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfNotWritable();
            if (ReceivedBytes < Size)
            {
                return new IncomingFileCompleteResult(
                    false,
                    null,
                    global::CSIViewer.Core.Localization.Format("Faltan {0} bytes para completar el archivo.",Size - ReceivedBytes));
            }

            PathValidator.EnsureSafeTransferDestination(_rootDirectory, FinalPath);
            PathValidator.EnsureNoReparsePoints(_rootDirectory, TempPath);
            PathValidator.EnsureNoReparsePoints(_rootDirectory, SidecarPath);
            _directoryLease.PrepareFinalization(FinalPath);
            await _stream!.FlushAsync(cancellationToken).ConfigureAwait(false);

            var actual = Convert.ToHexString(_hash!.GetHashAndReset());
            _hash.Dispose();
            _hash = null;
            _completed = true;

            if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
                _stream = null;
                _directoryLease.DeleteTemporary(Path.GetFileName(TempPath));
                _directoryLease.DeleteTemporary(Path.GetFileName(SidecarPath));
                return new IncomingFileCompleteResult(false, null, "integrity");
            }

            var target = _directoryLease.MoveToDestination(_stream.SafeFileHandle, Overwrite);
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;

            _directoryLease.DeleteTemporary(Path.GetFileName(SidecarPath));
            return new IncomingFileCompleteResult(true, target);
        }
        catch (UnauthorizedAccessException)
        {
            return new IncomingFileCompleteResult(false, null, "access_denied");
        }
        catch (IOException ex)
        {
            return new IncomingFileCompleteResult(false, null, MapIOException(ex));
        }
        catch (Exception)
        {
            return new IncomingFileCompleteResult(false, null, "io_error");
        }
        finally
        {
            if (_completed) ReleaseActiveKey();
            _gate.Release();
        }
    }

    public async Task AbortAsync(bool deletePartial, CancellationToken cancellationToken = default)
    {
        var released = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A completed/previously aborted instance no longer owns these
            // names; a new transfer may already be using its sidecar/temp.
            if (_disposed || Volatile.Read(ref _activeKeyReleased) != 0) return;
            if (_stream is not null)
            {
                try
                {
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // The stream may already be broken.
                }

                await _stream.DisposeAsync().ConfigureAwait(false);
                _stream = null;
            }

            _hash?.Dispose();
            _hash = null;

            if (deletePartial)
            {
                _directoryLease.DeleteTemporary(Path.GetFileName(TempPath));
                _directoryLease.DeleteTemporary(Path.GetFileName(SidecarPath));
            }
            released = true;
        }
        finally
        {
            if (released) ReleaseActiveKey();
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
                _stream = null;
            }

            _hash?.Dispose();
            _hash = null;
        }
        finally
        {
            _directoryLease.Dispose();
            ReleaseActiveKey();
            _gate.Release();
        }
    }

    private void ReleaseActiveKey()
    {
        if (Interlocked.Exchange(ref _activeKeyReleased, 1) == 0)
        {
            ActiveTempPaths.TryRemove(_activeKey, out _);
        }
    }

    private void ThrowIfNotWritable()
    {
        if (_disposed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        if (_completed)
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("La transferencia ya ha finalizado."));
        }

        if (_stream is null || _hash is null)
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("La transferencia no está abierta."));
        }
    }

    private static async Task<PartialMetadata?> ReadPartialAsync(
        TransferDirectoryLease directoryLease,
        string tempDirectory,
        string preferredSidecar,
        string root,
        string relativePath,
        long size,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (File.Exists(preferredSidecar))
        {
            candidates.Add(preferredSidecar);
        }

        try
        {
            candidates.AddRange(Directory.EnumerateFiles(tempDirectory, "*.json"));
        }
        catch
        {
            // The temp directory may not exist yet.
        }

        foreach (var sidecar in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                PathValidator.EnsureNoReparsePoints(root, sidecar);
                var json = await directoryLease.ReadMetadataAsync(Path.GetFileName(sidecar), cancellationToken).ConfigureAwait(false);
                if (json is null) continue;
                var metadata = JsonSerializer.Deserialize<PartialMetadata>(json);
                if (metadata is null ||
                    metadata.Size != size ||
                    !string.Equals(metadata.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(metadata.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Validate both sides against the current root. A tampered
                // sidecar must never redirect a write outside the download
                // folder or the partial directory.
                PathValidator.EnsureInsideRoot(root, metadata.FinalPath);
                PathValidator.EnsureInsideRoot(tempDirectory, metadata.TempPath);
                PathValidator.EnsureSafeTransferDestination(root, metadata.FinalPath);
                PathValidator.EnsureNoReparsePoints(root, metadata.TempPath);
                if (!string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(metadata.TempPath)),
                        Path.GetFullPath(tempDirectory),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                _ = directoryLease.TemporaryLength(Path.GetFileName(metadata.TempPath));

                return metadata with { SidecarPath = sidecar };
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Ignore an incomplete, corrupt or malicious sidecar.
            }
        }

        return null;
    }

    private static string ComputeKey(string relativePath, long size, string sha256)
    {
        var material = $"{relativePath.ToLowerInvariant()}|{size}|{sha256.ToLowerInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool HasEnoughFreeSpace(string root, long requiredBytes)
    {
        try
        {
            var pathRoot = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrWhiteSpace(pathRoot))
            {
                return true;
            }

            var drive = new DriveInfo(pathRoot);
            if (!drive.IsReady)
            {
                return true;
            }

            var available = drive.AvailableFreeSpace;
            return available > FreeSpaceReserveBytes &&
                   requiredBytes <= available - FreeSpaceReserveBytes;
        }
        catch
        {
            // Unknown filesystems (UNC, virtual drives) are allowed; the write
            // itself still fails safely if there is no space.
            return true;
        }
    }

    private static string MapIOException(IOException exception)
    {
        var message = exception.Message;
        return message.Contains("space", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("espacio", StringComparison.OrdinalIgnoreCase)
            ? "no_space"
            : "io_error";
    }

    private sealed record PartialMetadata(
        string TransferId,
        string RelativePath,
        string FinalPath,
        string TempPath,
        long Size,
        string Sha256,
        bool Overwrite = false,
        string? SidecarPath = null);
}
