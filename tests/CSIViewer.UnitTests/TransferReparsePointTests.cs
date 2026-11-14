using System.Diagnostics;
using System.Security.Cryptography;
using CSIViewer.Application;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class TransferReparsePointTests
{
    [TestMethod]
    [DataRow("nested")]
    [DataRow(".rdpart")]
    public async Task DirectorySymbolicLinksAreRejectedWhenCreationIsPermitted(string directory)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "csiviewer-symlink-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "downloads");
        var outside = Path.Combine(fixture, "outside");
        var link = Path.Combine(root, directory);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (UnauthorizedAccessException)
            {
                Assert.Inconclusive("Symlink creation is not permitted; do not enable Developer Mode or elevate for this test.");
                return;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 1314)
            {
                Assert.Inconclusive("Missing symbolic-link privilege; junction coverage is separate.");
                return;
            }
            var opened = await IncomingFileTransfer.OpenAsync(root, "symlink", directory == ".rdpart" ? "file.bin" : "nested/file.bin",
                0, Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
            if (opened.Transfer is not null) await opened.Transfer.DisposeAsync();
            Assert.AreEqual(IncomingFileOpenStatus.Rejected, opened.Status);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(outside).ToArray());
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(fixture, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("nested")]
    [DataRow(".rdpart")]
    public async Task IncomingTransferRejectsJunctionOutsideAuthorizedRoot(string linkedDirectory)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "csiviewer-junction-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "downloads");
        var outside = Path.Combine(fixture, "outside");
        var link = Path.Combine(root, linkedDirectory);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            // Junctions do not require enabling Developer Mode or elevation.
            // Both ends belong to this GUID fixture; no user paths are linked.
            await CreateJunctionAsync(link, outside);
            Assert.IsTrue(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));

            var relative = linkedDirectory == ".rdpart" ? "empty.bin" : "nested/empty.bin";
            var opened = await IncomingFileTransfer.OpenAsync(root, Guid.NewGuid().ToString("N"),
                relative, 0, Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
            if (opened.Transfer is not null) await opened.Transfer.DisposeAsync();

            Assert.AreEqual(IncomingFileOpenStatus.Rejected, opened.Status);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(outside).ToArray(), "No writes may escape the authorized root.");
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(fixture, recursive: true);
        }
    }

    [TestMethod]
    public async Task FinalizationRechecksDestinationDirectory()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "csiviewer-junction-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "downloads");
        var outside = Path.Combine(fixture, "outside");
        var link = Path.Combine(root, "nested");
        Directory.CreateDirectory(outside);
        try
        {
            var opened = await IncomingFileTransfer.OpenAsync(root, Guid.NewGuid().ToString("N"),
                "nested/empty.bin", 0, Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status);
            await using var transfer = opened.Transfer!;
            Directory.Delete(link);
            await CreateJunctionAsync(link, outside);

            var completed = await transfer.CompleteAsync();

            Assert.IsFalse(completed.Success);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(outside).ToArray());
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(fixture, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExplicitlyAuthorizedRedirectedRootRemainsUsable()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "csiviewer-junction-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "downloads");
        var target = Path.Combine(fixture, "target");
        Directory.CreateDirectory(target);
        try
        {
            await CreateJunctionAsync(root, target);
            var opened = await IncomingFileTransfer.OpenAsync(root, Guid.NewGuid().ToString("N"),
                "empty.bin", 0, Convert.ToHexString(SHA256.HashData([])), FileConflictPolicy.Rename, false);
            Assert.AreEqual(IncomingFileOpenStatus.Accepted, opened.Status);
            await using var transfer = opened.Transfer!;
            Assert.IsTrue((await transfer.CompleteAsync()).Success);
            Assert.IsTrue(File.Exists(Path.Combine(target, "empty.bin")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root);
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static async Task CreateJunctionAsync(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
    }
}
