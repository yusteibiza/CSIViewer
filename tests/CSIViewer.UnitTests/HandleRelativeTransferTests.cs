using System.Reflection;
using CSIViewer.Application;
using System.Security.Cryptography;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class HandleRelativeTransferTests
{
    [TestMethod]
    public async Task PreCanceledOpenCreatesNoDirectoriesOrPartials()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-canceled-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => IncomingFileTransfer.OpenAsync(
            root, "canceled", "nested/file.bin", 0, Convert.ToHexString(SHA256.HashData([])),
            FileConflictPolicy.Rename, false, cancellation.Token));
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task NativeLeaseRejectsHardlinkedResumedPartialWithoutChangingTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-resume-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "unrelated.bin");
        var bytes = "synthetic unrelated bytes"u8.ToArray();
        await File.WriteAllBytesAsync(target, bytes);
        try
        {
            var type = typeof(IncomingFileTransfer).Assembly.GetType("CSIViewer.Application.TransferDirectoryLease")!;
            using var lease = (IDisposable)Activator.CreateInstance(type, root)!;
            var partial = Path.Combine(root, ".rdpart", "linked.part");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/d /c mklink /H \"{partial}\" \"{target}\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            await process.WaitForExitAsync();
            Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
            var error = Assert.ThrowsExactly<TargetInvocationException>(() =>
                type.GetMethod("OpenTemporary")!.Invoke(lease, ["linked.part", true]));
            Assert.IsInstanceOfType<IOException>(error.InnerException);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task ConcurrentDirectoryAndJunctionSwapsNeverWriteOutsideRoot()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "csiviewer-native-race-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "downloads");
        var outside = Path.Combine(fixture, "outside");
        var normal = Path.Combine(root, "nested");
        var stash = Path.Combine(root, "stash");
        var redirect = Path.Combine(root, "redirect");
        Directory.CreateDirectory(normal);
        Directory.CreateDirectory(outside);
        var transfers = new List<IncomingFileTransfer>();
        using var stop = new CancellationTokenSource();
        Task attacker = Task.CompletedTask;
        var swaps = 0;
        try
        {
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/d /c mklink /J \"{redirect}\" \"{outside}\"", UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })!)
            {
                await process.WaitForExitAsync();
                Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
            }
            for (var index = 0; index < 32; index++)
            {
                var bytes = BitConverter.GetBytes(index);
                var opened = await IncomingFileTransfer.OpenAsync(root, index.ToString(), $"nested/file-{index}.bin", bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)), FileConflictPolicy.Rename, false);
                Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);
                await opened.Transfer!.AppendAsync(bytes);
                transfers.Add(opened.Transfer);
            }
            void Cycle()
            {
                try
                {
                    if (Directory.Exists(normal) && !File.GetAttributes(normal).HasFlag(FileAttributes.ReparsePoint) && !Directory.Exists(stash))
                        Directory.Move(normal, stash);
                    if (Directory.Exists(redirect) && !Directory.Exists(normal)) { Directory.Move(redirect, normal); Interlocked.Increment(ref swaps); }
                    if (Directory.Exists(normal) && File.GetAttributes(normal).HasFlag(FileAttributes.ReparsePoint) && !Directory.Exists(redirect))
                        Directory.Move(normal, redirect);
                    if (Directory.Exists(stash) && !Directory.Exists(normal)) Directory.Move(stash, normal);
                }
                catch (IOException) { } // leases may legitimately pin names during native operations
            }
            attacker = Task.Run(() => { while (!stop.IsCancellationRequested) Cycle(); });
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref swaps) > 10, TimeSpan.FromSeconds(5)));
            var results = await Task.WhenAll(transfers.Select(transfer => Task.Run(() => transfer.CompleteAsync())));
            stop.Cancel();
            await attacker;
            foreach (var transfer in transfers) await transfer.DisposeAsync();
            Cycle();
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(outside).ToArray(), "No bytes or empty files may escape to the junction target.");
            for (var index = 0; index < results.Length; index++)
                if (results[index].Success)
                    CollectionAssert.AreEqual(BitConverter.GetBytes(index), await File.ReadAllBytesAsync(results[index].FinalPath!));
        }
        finally
        {
            stop.Cancel();
            await attacker;
            foreach (var transfer in transfers) await transfer.DisposeAsync();
            foreach (var entry in new[] { normal, redirect, stash })
                if (Directory.Exists(entry) && File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(entry);
            Directory.Delete(fixture, recursive: true);
        }
    }
    [TestMethod]
    public async Task ReplacementWithOrdinaryDirectoryIsRejectedByIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-native-" + Guid.NewGuid().ToString("N"));
        try
        {
            var opened = await IncomingFileTransfer.OpenAsync(root, "identity", "nested/file.bin", 0,
                Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);
            await using var file = opened.Transfer!;
            Directory.Move(Path.Combine(root, "nested"), Path.Combine(root, "original"));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            Assert.IsFalse((await file.CompleteAsync()).Success);
            Assert.IsFalse(File.Exists(Path.Combine(root, "nested", "file.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "original", "file.bin")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task RepeatedAbortAndDisposeReleaseDirectoryHandles()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-native-" + Guid.NewGuid().ToString("N"));
        var renamed = root + "-renamed";
        try
        {
            for (var index = 0; index < 100; index++)
            {
                var opened = await IncomingFileTransfer.OpenAsync(root, index.ToString(), "nested/file.bin", 0,
                    Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
                Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status, opened.Reason);
                await using var file = opened.Transfer!;
                await file.AbortAsync(deletePartial: true);
                await Task.WhenAll(file.DisposeAsync().AsTask(), file.DisposeAsync().AsTask());
            }
            Directory.Move(root, renamed); // any leaked no-share-delete root handle fails this
            Directory.Delete(renamed, recursive: true);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(renamed)) Directory.Delete(renamed, recursive: true);
        }
    }
    [TestMethod]
    public async Task NativeLeaseCreatesWritesAndRenamesByHandle()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var type = typeof(IncomingFileTransfer).Assembly.GetType("CSIViewer.Application.TransferDirectoryLease")!;
            using var lease = (IDisposable)Activator.CreateInstance(type, root)!;
            type.GetMethod("SetDestination")!.Invoke(lease, [Path.Combine(root, "file.bin")]);
            await using var file = (FileStream)type.GetMethod("OpenTemporary")!.Invoke(lease, ["smoke.part", false])!;
            await file.WriteAsync("synthetic"u8.ToArray());
            await file.FlushAsync();
            await (Task)type.GetMethod("WriteMetadataAsync")!.Invoke(lease, ["smoke.json", "{}", CancellationToken.None])!;
            type.GetMethod("MoveToDestination")!.Invoke(lease, [file.SafeFileHandle, false]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
