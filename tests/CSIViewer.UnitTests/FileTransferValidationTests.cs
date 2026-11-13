using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class FileTransferValidationTests
{
    [TestMethod]
    public void TransferPath_AcceptsNestedRelativePath()
    {
        Assert.IsTrue(MessageValidation.IsValidTransferPath("folder/sub/file.txt"));
        Assert.IsTrue(MessageValidation.IsValidTransferPath("file.txt"));
    }

    [TestMethod]
    public void TransferPath_RejectsTraversalAbsoluteAndInvalidCharacters()
    {
        Assert.IsFalse(MessageValidation.IsValidTransferPath("../escape.txt"));
        Assert.IsFalse(MessageValidation.IsValidTransferPath("folder/../../escape.txt"));
        Assert.IsFalse(MessageValidation.IsValidTransferPath(@"C:\Windows\file.txt"));
        Assert.IsFalse(MessageValidation.IsValidTransferPath("folder/file.txt:stream"));
        Assert.IsFalse(MessageValidation.IsValidTransferPath("folder/\0file.txt"));
        Assert.IsFalse(MessageValidation.IsValidTransferPath(""));
        Assert.IsTrue(MessageValidation.IsValidTransferPath("", allowEmpty: true));
    }

    [TestMethod]
    public void PathValidator_RejectsReservedAndInvalidNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "CSIViewerPathTests");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, "CON.txt"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, "folder/NUL"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, "CON.tar.gz"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, "folder/file.txt:stream"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, "folder/../escape.txt"));
    }

    [TestMethod]
    public void PathValidator_EnsureInsideRootRejectsOutsidePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "CSIViewerPathTests");
        var inside = Path.Combine(root, "file.txt");
        Assert.AreEqual(Path.GetFullPath(inside), PathValidator.EnsureInsideRoot(root, inside));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PathValidator.EnsureInsideRoot(root, Path.Combine(root, "..", "escape.txt")));
    }

    [TestMethod]
    public void FileChunkPayload_RoundTripsHeaderAndData()
    {
        var transferId = Guid.NewGuid();
        var data = new byte[4096];
        Random.Shared.NextBytes(data);

        var payload = FileChunkPayload.Create(transferId, 7, 123456, data);
        Assert.IsTrue(FileChunkPayload.TryRead(
            payload,
            out var parsedId,
            out var fileIndex,
            out var offset,
            out var parsedData));

        Assert.AreEqual(transferId, parsedId);
        Assert.AreEqual(7, fileIndex);
        Assert.AreEqual(123456, offset);
        Assert.IsTrue(parsedData.SequenceEqual(data));
    }

    [TestMethod]
    public void TransferItem_ProgressStateAndEtaAreConsistent()
    {
        var item = new TransferItem
        {
            Id = "1",
            FileName = "file.bin",
            Direction = TransferDirection.Send,
            TotalBytes = 10_000,
            TransferredBytes = 5_000,
            BytesPerSecond = 1_000
        };

        Assert.AreEqual(0.5, item.Progress, 0.001);
        Assert.AreEqual("Envío", item.DirectionText);
        Assert.IsTrue(item.IsActive);
        Assert.IsTrue(item.CanCancel);
        Assert.IsFalse(item.CanPause);
        Assert.IsTrue(item.EtaText.Contains('5'));

        item.State = TransferState.Paused;
        Assert.IsTrue(item.CanResume);
        Assert.AreEqual("Pausada", item.StateText);

        item.State = TransferState.Interrupted;
        Assert.IsTrue(item.CanResume);
        Assert.IsTrue(item.CanCancel);
        Assert.AreEqual("Interrumpida", item.StateText);

        item.State = TransferState.Completed;
        Assert.IsFalse(item.CanCancel);
        Assert.AreEqual("Completada", item.StateText);
    }
}
