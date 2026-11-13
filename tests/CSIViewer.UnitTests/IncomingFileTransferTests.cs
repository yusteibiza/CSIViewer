using System.Security.Cryptography;
using CSIViewer.Application;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class IncomingFileTransferTests
{
    [TestMethod]
    public async Task LargeFile_StreamsChunksAndVerifiesSha()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source.bin");
            const long size = 16L * 1024 * 1024;
            var sha = await CreateRandomFileAsync(source, size);

            var opened = await IncomingFileTransfer.OpenAsync(
                Path.Combine(root, "downloads"),
                Guid.NewGuid().ToString("N"),
                "large.bin",
                size,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);
            Assert.IsNotNull(opened.Transfer);
            var transfer = opened.Transfer!;

            await using (transfer)
            {
                await PumpAsync(source, transfer, 256 * 1024);
                var result = await transfer.CompleteAsync();
                Assert.IsTrue(result.Success, result.Error);
                Assert.IsNotNull(result.FinalPath);
                Assert.AreEqual(size, new FileInfo(result.FinalPath).Length);
                Assert.AreEqual(sha, await ComputeShaAsync(result.FinalPath));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task SimulatedDisconnect_ResumesFromDurablePrefix()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source.bin");
            const long size = 12L * 1024 * 1024;
            const long firstPart = 5L * 1024 * 1024;
            var sha = await CreateRandomFileAsync(source, size);
            var downloadRoot = Path.Combine(root, "downloads");

            var first = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "resume.bin",
                size,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, first.Status, first.Reason);
            Assert.IsNotNull(first.Transfer);

            await PumpAsync(source, first.Transfer, 1024 * 1024, firstPart);
            Assert.AreEqual(firstPart, first.Transfer.ReceivedBytes);
            var tempPath = first.Transfer.TempPath;
            var sidecarPath = first.Transfer.SidecarPath;
            await first.Transfer.AbortAsync(deletePartial: false);
            await first.Transfer.DisposeAsync();

            Assert.IsTrue(File.Exists(tempPath), "El archivo parcial debe conservarse.");
            Assert.IsTrue(File.Exists(sidecarPath), "El sidecar debe conservarse.");

            var second = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "resume.bin",
                size,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, second.Status, second.Reason);
            Assert.IsNotNull(second.Transfer);
            Assert.AreEqual(firstPart, second.ResumeOffset);

            var resumedTransfer = second.Transfer!;
            await using (resumedTransfer)
            {
                await PumpAsync(source, resumedTransfer, 1024 * 1024, long.MaxValue, skip: firstPart);
                var result = await resumedTransfer.CompleteAsync();
                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual(sha, await ComputeShaAsync(result.FinalPath!));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Overwrite_RequiresExplicitConfirmation()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            Directory.CreateDirectory(downloadRoot);
            var destination = Path.Combine(downloadRoot, "existing.txt");
            await File.WriteAllTextAsync(destination, "old");
            var data = "new content"u8.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(data));

            var rejected = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "existing.txt",
                data.Length,
                sha,
                FileConflictPolicy.Overwrite,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Rejected, rejected.Status);
            Assert.AreEqual("overwrite_required", rejected.Reason);

            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "existing.txt",
                data.Length,
                sha,
                FileConflictPolicy.Overwrite,
                overwriteConfirmed: true);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);
            var overwriteTransfer = opened.Transfer!;
            await using (overwriteTransfer)
            {
                await overwriteTransfer.AppendAsync(data);
                var result = await overwriteTransfer.CompleteAsync();
                Assert.IsTrue(result.Success, result.Error);
            }

            Assert.AreEqual("new content", await File.ReadAllTextAsync(destination));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Duplicate_RenameKeepsBothFiles()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            Directory.CreateDirectory(downloadRoot);
            var destination = Path.Combine(downloadRoot, "same.txt");
            await File.WriteAllTextAsync(destination, "original");
            var data = "copy"u8.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(data));

            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "same.txt",
                data.Length,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);
            var renameTransfer = opened.Transfer!;
            await using (renameTransfer)
            {
                await renameTransfer.AppendAsync(data);
                var result = await renameTransfer.CompleteAsync();
                Assert.IsTrue(result.Success, result.Error);
                Assert.AreNotEqual(destination, result.FinalPath);
            }

            Assert.AreEqual("original", await File.ReadAllTextAsync(destination));
            Assert.AreEqual("copy", await File.ReadAllTextAsync(
                Directory.GetFiles(downloadRoot, "same (*).txt").Single()));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Duplicate_SkipDoesNotCreateTransfer()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            Directory.CreateDirectory(downloadRoot);
            await File.WriteAllTextAsync(Path.Combine(downloadRoot, "skip.txt"), "original");
            var sha = Convert.ToHexString(SHA256.HashData("x"u8));

            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "skip.txt",
                1,
                sha,
                FileConflictPolicy.Skip,
                overwriteConfirmed: false);

            Assert.AreEqual(IncomingFileOpenStatus.Skipped, opened.Status);
            Assert.AreEqual("conflict_skipped", opened.Reason);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task TraversalPath_IsRejected()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            var sha = Convert.ToHexString(SHA256.HashData("x"u8));

            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "../escape.txt",
                1,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);

            Assert.AreEqual(IncomingFileOpenStatus.Rejected, opened.Status);
            Assert.AreEqual("invalid_path", opened.Reason);
            Assert.IsFalse(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task ShaMismatch_DiscardsPartialFile()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            var data = "actual"u8.ToArray();
            var wrongSha = Convert.ToHexString(SHA256.HashData("expected"u8));
            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "bad.bin",
                data.Length,
                wrongSha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);

            await opened.Transfer!.AppendAsync(data);
            var result = await opened.Transfer.CompleteAsync();
            Assert.IsFalse(result.Success);
            Assert.AreEqual("integrity", result.Error);

            await opened.Transfer.DisposeAsync();
            Assert.IsFalse(File.Exists(Path.Combine(downloadRoot, "bad.bin")));
            Assert.AreEqual(0, Directory.GetFiles(Path.Combine(downloadRoot, ".rdpart")).Length);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task ZeroByteFile_Completes()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            var sha = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "empty.txt",
                0,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);

            var emptyTransfer = opened.Transfer!;
            await using (emptyTransfer)
            {
                var result = await emptyTransfer.CompleteAsync();
                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual(0, new FileInfo(result.FinalPath!).Length);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task InsufficientSpace_IsRejected()
    {
        var root = CreateRoot();
        try
        {
            var sha = Convert.ToHexString(SHA256.HashData("x"u8));
            var opened = await IncomingFileTransfer.OpenAsync(
                root,
                Guid.NewGuid().ToString("N"),
                "huge.bin",
                long.MaxValue,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);

            Assert.AreEqual(IncomingFileOpenStatus.Rejected, opened.Status);
            Assert.AreEqual("no_space", opened.Reason);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task LockedDestination_ReturnsControlledError()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            Directory.CreateDirectory(downloadRoot);
            var destination = Path.Combine(downloadRoot, "locked.bin");
            await File.WriteAllTextAsync(destination, "original");
            var data = "new"u8.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(data));

            await using var locked = new FileStream(
                destination,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var opened = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "locked.bin",
                data.Length,
                sha,
                FileConflictPolicy.Overwrite,
                overwriteConfirmed: true);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);

            var transfer = opened.Transfer!;
            await using (transfer)
            {
                await transfer.AppendAsync(data);
                var result = await transfer.CompleteAsync();
                Assert.IsFalse(result.Success);
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task ConcurrentOpenForSameFile_IsRejectedUntilReleased()
    {
        var root = CreateRoot();
        try
        {
            var downloadRoot = Path.Combine(root, "downloads");
            var data = "busy"u8.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(data));

            var first = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "busy.bin",
                data.Length,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, first.Status, first.Reason);

            var second = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "busy.bin",
                data.Length,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Rejected, second.Status);
            Assert.AreEqual("busy", second.Reason);

            await first.Transfer!.AbortAsync(deletePartial: true);
            await first.Transfer.DisposeAsync();

            var third = await IncomingFileTransfer.OpenAsync(
                downloadRoot,
                Guid.NewGuid().ToString("N"),
                "busy.bin",
                data.Length,
                sha,
                FileConflictPolicy.Rename,
                overwriteConfirmed: false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, third.Status, third.Reason);
            await third.Transfer!.DisposeAsync();
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CSIViewerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Test cleanup is best effort.
        }
    }

    private static async Task<string> CreateRandomFileAsync(string path, long size)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            256 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[256 * 1024];
        long written = 0;
        while (written < size)
        {
            var count = (int)Math.Min(buffer.Length, size - written);
            RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
            await stream.WriteAsync(buffer.AsMemory(0, count));
            hash.AppendData(buffer, 0, count);
            written += count;
        }

        await stream.FlushAsync();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task PumpAsync(
        string source,
        IncomingFileTransfer transfer,
        int chunkSize,
        long maxBytes = long.MaxValue,
        long skip = 0)
    {
        await using var stream = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            chunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (skip > 0)
        {
            stream.Seek(skip, SeekOrigin.Begin);
        }

        var remaining = Math.Max(0, Math.Min(maxBytes, stream.Length - skip));
        var buffer = new byte[chunkSize];
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, count));
            if (read <= 0)
            {
                break;
            }

            await transfer.AppendAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }
    }

    private static async Task<string> ComputeShaAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
