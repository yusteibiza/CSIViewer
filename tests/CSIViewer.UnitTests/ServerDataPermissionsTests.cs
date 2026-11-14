using System.Security.AccessControl;
using CSIViewer.Server;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ServerDataPermissionsTests
{
    [TestMethod]
    public void InaccessibleDatabaseFailsWithoutRecursiveAclRepair()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-permission-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var directory = new DirectoryInfo(root);
            var before = directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            using var locked = new FileStream(Path.Combine(root, "server.db"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => ServerDataPermissions.EnsureWritable(locked.Name));
            Assert.AreEqual(before, directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
