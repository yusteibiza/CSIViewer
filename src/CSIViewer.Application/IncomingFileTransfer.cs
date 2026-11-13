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
    private long _lastFlushedBytes;
    private int _activeKeyReleased;
    private bool _completed;
    private bool _disposed;

    private IncomingFileTransfer(
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
        try
        {
            var root = Path.GetFullPath(rootDirectory);            Directory.CreateDirectory(root);
            var baseFinalPath = PathValidator.GetSafeDestinationPath(root, relativePath);
            var tempDirectory = Path.Combine(root, ".rdpart");
            Directory.CreateDirectory(tempDirectory);

            var key = ComputeKey(relativePath, size, expectedSha256);
            var sidecarPath = Path.Combine(tempDirectory, key + ".json");
            var partial = await ReadPartialAsync(
                    tempDirectory,
                    sidecarPath,
                    root,
                    relativePath,
                    size,
                    expectedSha256,
                    cancellationToken)
                .ConfigureAwait(false);

            var tempPath = partial?.TempPath ?? Path.Combine(tempDirectory, key + ".part");
            var resumeOffset = 0L;
            if (partial is not null && File.Exists(tempPath))
            {
                var length = new FileInfo(tempPath).Length;
                if (length >= 0 && length <= size)
                {
                    resumeOffset = length;
                }
                else
                {
                    TryDelete(tempPath);
                    TryDelete(partial.SidecarPath);
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
            else if (Exists(baseFinalPath))
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
                        finalPath = GetUniquePath(baseFinalPath);
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

            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            activeTempPath = tempPath;
            if (!ActiveTempPaths.TryAdd(tempPath, 0))
            {
                return new IncomingFileOpenResult(IncomingFileOpenStatus.Rejected, Reason: "busy");
            }

            activeRegistered = true;

            if (partial is not null && File.Exists(tempPath) && resumeOffset > 0)
            {
                stream = new FileStream(
                    tempPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    StreamBufferBytes,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
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
                            throw new InvalidDataException("El archivo parcial es mayor que el tamaño declarado.");
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
                TryDelete(tempPath);
                stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    StreamBufferBytes,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
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
            await WriteSidecarAsync(sidecarPath, metadata, cancellationToken).ConfigureAwait(false);

            activeRegistered = false;
            return new IncomingFileOpenResult(
                IncomingFileOpenStatus.Accepted,
                new IncomingFileTransfer(
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
            if (ReceivedBytes + data.Length > Size)
            {
                throw new InvalidDataException("El bloque supera el tamaño declarado del archivo.");
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
                    $"Faltan {Size - ReceivedBytes} bytes para completar el archivo.");
            }

            await _stream!.FlushAsync(cancellationToken).ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;

            var actual = Convert.ToHexString(_hash!.GetHashAndReset());
            _hash.Dispose();
            _hash = null;
            _completed = true;
            ReleaseActiveKey();

            if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(TempPath);
                TryDelete(SidecarPath);
                return new IncomingFileCompleteResult(false, null, "integrity");
            }

            var target = FinalPath;
            try
            {
                if (Overwrite)
                {
                    File.Move(TempPath, target, overwrite: true);
                }
                else
                {
                    File.Move(TempPath, target, overwrite: false);
                }
            }
            catch (IOException) when (!Overwrite && Exists(target))
            {
                // Another process created the destination while we were
                // receiving. Keep the verified data and choose a new name.
                target = GetUniquePath(target);
                File.Move(TempPath, target, overwrite: false);
            }

            TryDelete(SidecarPath);
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
            _gate.Release();
        }
    }

    public async Task AbortAsync(bool deletePartial, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
                TryDelete(TempPath);
                TryDelete(SidecarPath);
            }
        }
        finally
        {
            _gate.Release();
            ReleaseActiveKey();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

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
            _gate.Release();
            _gate.Dispose();
            ReleaseActiveKey();
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
            throw new InvalidOperationException("La transferencia ya ha finalizado.");
        }

        if (_stream is null || _hash is null)
        {
            throw new InvalidOperationException("La transferencia no está abierta.");
        }
    }

    private static async Task<PartialMetadata?> ReadPartialAsync(
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
                var json = await File.ReadAllTextAsync(sidecar, cancellationToken).ConfigureAwait(false);
                var metadata = JsonSerializer.Deserialize<PartialMetadata>(json);
                if (metadata is null ||
                    metadata.Size != size ||
                    !string.Equals(metadata.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(metadata.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(metadata.TempPath))
                {
                    continue;
                }

                // Validate both sides against the current root. A tampered
                // sidecar must never redirect a write outside the download
                // folder or the partial directory.
                PathValidator.EnsureInsideRoot(root, metadata.FinalPath);
                PathValidator.EnsureInsideRoot(tempDirectory, metadata.TempPath);
                if (!string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(metadata.TempPath)),
                        Path.GetFullPath(tempDirectory),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return metadata with { SidecarPath = sidecar };
            }
            catch
            {
                // Ignore an incomplete, corrupt or malicious sidecar.
            }
        }

        return null;
    }

    private static async Task WriteSidecarAsync(
        string path,
        PartialMetadata metadata,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(metadata);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
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

    private static string GetUniquePath(string path)
    {
        if (!Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("No se pudo encontrar un nombre de archivo libre.");
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
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
