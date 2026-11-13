using CSIViewer.Application;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public void Normalize_ClampsValuesAndRestoresDefaults()
    {
        var settings = new AppSettings
        {
            ServerUrl = " ",
            DeviceName = " ",
            ImageQuality = 500,
            Fps = 0,
            MaxWidth = 99999,
            UploadLimitKbps = -10,
            HistoryRetentionDays = 0,
            Language = "fr",
            FileConflictPolicy = (FileConflictPolicy)99
        };

        settings.Normalize();

        Assert.AreEqual("http://localhost:5080", settings.ServerUrl);
        Assert.IsFalse(string.IsNullOrWhiteSpace(settings.DeviceName));
        Assert.AreEqual(95, settings.ImageQuality);
        Assert.AreEqual(1, settings.Fps);
        Assert.AreEqual(2560, settings.MaxWidth);
        Assert.AreEqual(0, settings.UploadLimitKbps);
        Assert.AreEqual(1, settings.HistoryRetentionDays);
        Assert.AreEqual("es", settings.Language);
        Assert.AreEqual(FileConflictPolicy.Rename, settings.FileConflictPolicy);
    }

    [TestMethod]
    public void Clone_DoesNotShareTheSameInstance()
    {
        var settings = new AppSettings { DeviceName = "Equipo A" };
        var clone = settings.Clone();
        clone.DeviceName = "Equipo B";
        Assert.AreEqual("Equipo A", settings.DeviceName);
        Assert.AreEqual("Equipo B", clone.DeviceName);
    }
}
