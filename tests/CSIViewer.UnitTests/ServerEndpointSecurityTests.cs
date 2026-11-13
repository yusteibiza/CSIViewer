using CSIViewer.Networking;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ServerEndpointSecurityTests
{
    private static void AssertRejected(string url, bool allowInsecure)
    {
        try
        {
            ServerEndpointSecurity.EnsureAllowed(url, allowInsecure);
            Assert.Fail("Se esperaba un rechazo de transporte para " + url);
        }
        catch (InvalidOperationException)
        {
            // Expected.
        }
    }

    [TestMethod]
    public void LoopbackHttp_IsAllowedForDevelopment()
    {
        ServerEndpointSecurity.EnsureAllowed("http://localhost:5080", allowInsecure: false);
        ServerEndpointSecurity.EnsureAllowed("http://127.0.0.1:5080", allowInsecure: false);
    }

    [TestMethod]
    public void RemoteHttp_IsRejectedByDefault()
    {
        AssertRejected("http://192.168.1.10:5080", allowInsecure: false);
        AssertRejected("http://example.com", allowInsecure: false);
    }

    [TestMethod]
    public void RemoteHttps_IsAllowed()
    {
        ServerEndpointSecurity.EnsureAllowed("https://remote.example.com:5080", allowInsecure: false);
        ServerEndpointSecurity.EnsureAllowed("wss://remote.example.com:5080", allowInsecure: false);
    }

    [TestMethod]
    public void ExplicitOptIn_AllowsRemoteHttp()
    {
        ServerEndpointSecurity.EnsureAllowed("http://192.168.1.10:5080", allowInsecure: true);
    }
}
