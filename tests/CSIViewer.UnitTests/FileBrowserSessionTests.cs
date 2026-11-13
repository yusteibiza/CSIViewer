using System.Reflection;
using CSIViewer.Core;
using CSIViewer.Security;
using CSIViewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class FileBrowserSessionTests
{
    [TestMethod]
    public async Task MissingSessionCannotBrowseOrPull()
    {
        await using var client = NewClient();
        var id = Guid.NewGuid().ToString();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.BrowseRemoteFilesAsync(id, ""));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.RequestRemoteFilesAsync(id, [@"C:\a"], @"C:\"));
    }

    [TestMethod]
    public async Task DestinationTokenIsBoundToSessionAndInvalidatedOnDisconnect()
    {
        await using var client = NewClient();
        var first = NewSession();
        var second = NewSession();
        var type = first.GetType();
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-grant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var register = typeof(RemoteClient).GetMethod("RegisterFileDestination", BindingFlags.Static | BindingFlags.NonPublic)!;
            var resolve = typeof(RemoteClient).GetMethod("ResolveFileDestination", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var token = (string)register.Invoke(null, [first, root])!;
            Assert.AreEqual(token, register.Invoke(null, [first, root]), "Repeated navigation should reuse the grant.");
            Assert.AreEqual(root, resolve.Invoke(client, [first, token]));
            var wrong = Assert.ThrowsExactly<TargetInvocationException>(() => resolve.Invoke(client, [second, token]));
            Assert.IsInstanceOfType<IOException>(wrong.InnerException);
            type.GetMethod("MarkDisconnecting")!.Invoke(first, null);
            var expired = Assert.ThrowsExactly<TargetInvocationException>(() => resolve.Invoke(client, [first, token]));
            Assert.IsInstanceOfType<IOException>(expired.InnerException);
        }
        finally
        {
            ((IDisposable)first).Dispose();
            ((IDisposable)second).Dispose();
            Directory.Delete(root);
        }
    }

    private static RemoteClient NewClient() => new(new AppState(), null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);

    private static object NewSession()
    {
        var type = typeof(RemoteClient).GetNestedType("RemoteSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(type, nonPublic: true)!;
        var id = Guid.NewGuid();
        void Set(string name, object value) => type.GetProperty(name)!.SetValue(session, value);
        Set("Id", id); Set("RemoteDeviceId", "123456789"); Set("RemoteName", "QA");
        Set("Permissions", SessionPermissions.SendFiles | SessionPermissions.ReceiveFiles);
        Set("IsController", true); Set("StartedUtc", DateTimeOffset.UtcNow);
        Set("Handshake", new SessionHandshake());
        Set("Crypto", new SessionCrypto(id, true, new SessionKeys(new byte[32], new byte[32], new byte[32])));
        type.GetMethod("MarkConnected")!.Invoke(session, null);
        return session;
    }
}
