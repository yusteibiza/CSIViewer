using System.Reflection;
using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class PipelineDumpRequestTests
{
    [TestMethod]
    public void LockedRequestIsRetriedWithoutDeletingOrThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "csiviewer-dump-" + Guid.NewGuid().ToString("N") + ".request");
        var method = typeof(RemoteClient).GetMethod("TryConsumePipelineDumpRequest", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Consume() => (bool)method.Invoke(null, [path])!;
        try
        {
            Assert.IsFalse(Consume());
            using (var held = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                held.WriteByte(1);
                Assert.IsFalse(Consume());
                Assert.IsTrue(File.Exists(path));
            }
            Assert.IsTrue(Consume());
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(Consume());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
