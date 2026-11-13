using CSIViewer.Application;
using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class FileBrowserTests
{
    [TestMethod]
    [DataRow(@"relative\file.txt")]
    [DataRow(@"C:relative")]
    [DataRow(@"\\server\share")]
    [DataRow(@"\\?\C:\Windows")]
    [DataRow(@"C:\Windows\..\Users")]
    [DataRow(@"C:\Windows\file:stream")]
    [DataRow(@"C:\Windows\bad.")]
    public void RejectsUnsafePaths(string path)
    {
        Assert.ThrowsExactly<IOException>(() => FileBrowser.ValidatePath(path));
    }

    [TestMethod]
    public void BrowseReturnsFilesFoldersAndParentWithoutOpeningFiles()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Carpeta á"));
            Directory.CreateDirectory(Path.Combine(root, ".rdpart"));
            File.WriteAllText(Path.Combine(root, "uno.txt"), "hello");
            var result = FileBrowser.Browse(root);
            Assert.AreEqual(root, result.Path);
            Assert.AreEqual(Directory.GetParent(root)!.FullName, result.ParentPath);
            Assert.HasCount(2, result.Entries);
            Assert.IsTrue(result.Entries[0].IsDirectory);
            Assert.AreEqual(5L, result.Entries[1].Size);
            Assert.IsFalse(result.HasMore);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void PagingIsBoundedAndCoversDirectory()
    {
        var root = CreateRoot();
        try
        {
            for (var i = 0; i < FileBrowser.PageSize + 3; i++) File.WriteAllText(Path.Combine(root, $"{i:D4}.txt"), "");
            var first = FileBrowser.Browse(root);
            var second = FileBrowser.Browse(root, FileBrowser.PageSize);
            Assert.HasCount(FileBrowser.PageSize, first.Entries);
            Assert.IsTrue(first.HasMore);
            Assert.HasCount(3, second.Entries);
            Assert.IsFalse(second.HasMore);
            Assert.AreEqual(FileBrowser.PageSize + 3, first.Entries.Concat(second.Entries).Select(x => x.FullPath).Distinct().Count());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void NegativeOffsetIsRejected() => Assert.ThrowsExactly<IOException>(() => FileBrowser.Browse("", -1));

    [TestMethod]
    public void DriveRootsDoNotGrantDestination()
    {
        var result = FileBrowser.Browse("");
        Assert.IsNull(result.DestinationToken);
        Assert.IsNull(result.ParentPath);
        Assert.IsTrue(result.Entries.All(x => x.IsDirectory));
    }

    [TestMethod]
    public void OldOfferRemainsReadableWithoutDestination()
    {
        var bytes = ControlJson.Create("file.offer", new { TransferId = Guid.NewGuid().ToString("N"), FileIndex = 0,
            FileName = "a.txt", Size = 0L, Sha256 = new string('0', 64), RelativePath = "a.txt" });
        Assert.IsTrue(ControlJson.TryRead(bytes, out var envelope));
        Assert.IsNull(ControlJson.Payload<FileOfferPayload>(envelope!)!.DestinationToken);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-browser-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
