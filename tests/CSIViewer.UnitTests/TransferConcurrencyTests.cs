using System.Security.Cryptography;
using CSIViewer.Application;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class TransferConcurrencyTests
{
    [TestMethod]
    public async Task PredictableSidecarTempHardlinkCannotTruncateUnrelatedFile()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "csiviewer-hardlink-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "downloads");
        Directory.CreateDirectory(Path.Combine(root, ".rdpart"));
        var outside = Path.Combine(fixture, "sentinel.txt");
        try
        {
            await File.WriteAllTextAsync(outside, "synthetic sentinel");
            var sha = Convert.ToHexString(SHA256.HashData([]));
            var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("empty.bin|0|" + sha.ToLowerInvariant()))).ToLowerInvariant();
            var link = Path.Combine(root, ".rdpart", key + ".json.tmp");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/d /c mklink /H \"{link}\" \"{outside}\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            await process.WaitForExitAsync();
            Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
            var opened = await IncomingFileTransfer.OpenAsync(root, "synthetic", "empty.bin", 0, sha, FileConflictPolicy.Rename, false);
            if (opened.Transfer is not null) await opened.Transfer.DisposeAsync();

            Assert.AreEqual("synthetic sentinel", await File.ReadAllTextAsync(outside));
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    [TestMethod]
    public async Task CompletedTransferCannotDeleteNewTransferSidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-transfer-race-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sha = Convert.ToHexString(SHA256.HashData([]));
            var first = await IncomingFileTransfer.OpenAsync(root, "first", "same.bin", 0, sha, FileConflictPolicy.Rename, false);
            await using var old = first.Transfer!;
            Assert.IsTrue((await old.CompleteAsync()).Success);
            var second = await IncomingFileTransfer.OpenAsync(root, "second", "same.bin", 0, sha, FileConflictPolicy.Rename, false);
            await using var current = second.Transfer!;

            await old.AbortAsync(deletePartial: true);

            Assert.IsTrue(File.Exists(current.SidecarPath), "A stale instance must not delete another transfer's metadata.");
            Assert.IsTrue((await current.CompleteAsync()).Success);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [DataRow(".rdpart/metadata.json")]
    [DataRow(".RDPART/nested/partial.part")]
    public async Task RemoteNamesCannotTargetInternalTransferDirectory(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-transfer-race-" + Guid.NewGuid().ToString("N"));
        try
        {
            var opened = await IncomingFileTransfer.OpenAsync(root, "reserved", name, 0,
                Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
            if (opened.Transfer is not null) await opened.Transfer.DisposeAsync();
            Assert.AreEqual(IncomingFileOpenStatus.Rejected, opened.Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task ConcurrentConflictingFinalizationsPreserveEveryPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-transfer-race-" + Guid.NewGuid().ToString("N"));
        var transfers = new List<IncomingFileTransfer>();
        try
        {
            for (var index = 0; index < 32; index++)
            {
                var bytes = BitConverter.GetBytes(index);
                var opened = await IncomingFileTransfer.OpenAsync(root, index.ToString(), "same.bin", bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)), FileConflictPolicy.Rename, false);
                Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status);
                transfers.Add(opened.Transfer!);
                await opened.Transfer!.AppendAsync(bytes);
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = transfers.Select(transfer => Task.Run(async () =>
            {
                await start.Task;
                return await transfer.CompleteAsync();
            })).ToArray();
            start.SetResult();
            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsTrue(results.All(result => result.Success), string.Join(",", results.Select(result => result.Error)));
            Assert.AreEqual(32, results.Select(result => result.FinalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var received = new List<int>();
            foreach (var result in results) received.Add(BitConverter.ToInt32(await File.ReadAllBytesAsync(result.FinalPath!)));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 32).ToArray(), received.ToArray());
        }
        finally
        {
            foreach (var transfer in transfers) await transfer.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
