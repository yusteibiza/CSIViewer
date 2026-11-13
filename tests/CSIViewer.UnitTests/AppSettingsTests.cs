using CSIViewer.Application;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void ReconnectionDefaults_AreSafe()
    {
        var settings = new AppSettings();
        settings.Normalize();

        Assert.AreEqual(10, settings.ReconnectMaxRetries);
        Assert.AreEqual(2, settings.ReconnectInitialDelaySeconds);
        Assert.AreEqual(30, settings.ReconnectMaxDelaySeconds);
        Assert.AreEqual(20, settings.HeartbeatTimeoutSeconds);
        Assert.AreEqual(5, settings.HeartbeatIntervalSeconds);
        Assert.AreEqual(10, settings.ConnectTimeoutSeconds);
        Assert.AreEqual(60, settings.TemporaryPasswordLifetimeMinutes);
        Assert.IsFalse(settings.AllowInsecureServer);
    }

    [TestMethod]
    public void Normalize_ClampsTimeoutsAndRetries()
    {
        var settings = new AppSettings
        {
            ConnectTimeoutSeconds = 1,
            MessageTimeoutSeconds = 1,
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 1,
            ReconnectMaxRetries = -5,
            ReconnectInitialDelaySeconds = 0,
            ReconnectMaxDelaySeconds = 0,
            ReconnectJitterPercent = 99,
            TemporaryPasswordLifetimeMinutes = -10
        };

        settings.Normalize();

        Assert.AreEqual(3, settings.ConnectTimeoutSeconds);
        Assert.AreEqual(10, settings.MessageTimeoutSeconds);
        Assert.AreEqual(2, settings.HeartbeatIntervalSeconds);
        Assert.AreEqual(5, settings.HeartbeatTimeoutSeconds);
        Assert.AreEqual(0, settings.ReconnectMaxRetries);
        Assert.AreEqual(1, settings.ReconnectInitialDelaySeconds);
        Assert.AreEqual(1, settings.ReconnectMaxDelaySeconds);
        Assert.AreEqual(50, settings.ReconnectJitterPercent);
        Assert.AreEqual(0, settings.TemporaryPasswordLifetimeMinutes);
    }

    [TestMethod]
    public void Normalize_KeepsHeartbeatTimeoutAboveInterval()
    {
        var settings = new AppSettings
        {
            HeartbeatIntervalSeconds = 60,
            HeartbeatTimeoutSeconds = 30
        };

        settings.Normalize();

        Assert.IsTrue(settings.HeartbeatTimeoutSeconds >= settings.HeartbeatIntervalSeconds + 3);
    }

    [TestMethod]
    public void Normalize_KeepsMaxDelayAboveInitialDelay()
    {
        var settings = new AppSettings
        {
            ReconnectInitialDelaySeconds = 20,
            ReconnectMaxDelaySeconds = 5
        };

        settings.Normalize();

        Assert.AreEqual(20, settings.ReconnectMaxDelaySeconds);
    }
}
