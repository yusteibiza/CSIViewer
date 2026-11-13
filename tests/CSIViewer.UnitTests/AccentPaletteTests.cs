using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class AccentPaletteTests
{
    [TestMethod]
    [DataRow(AccentColor.System)]
    [DataRow(AccentColor.Blue)]
    [DataRow(AccentColor.Green)]
    [DataRow(AccentColor.Teal)]
    [DataRow(AccentColor.Purple)]
    [DataRow(AccentColor.Pink)]
    [DataRow(AccentColor.Orange)]
    [DataRow(AccentColor.Red)]
    public void AccentForegroundMeetsReadableContrastInBothThemes(AccentColor choice)
    {
        foreach (var dark in new[] { false, true })
        {
            var color = AccentPalette.Resolve(choice, dark, 0x0078D4);
            Assert.IsTrue(AccentPalette.Contrast(color, AccentPalette.Foreground(color)) >= 4.5);
        }
    }

    [TestMethod]
    [DataRow(AccentColor.System)]
    [DataRow(AccentColor.Blue)]
    [DataRow(AccentColor.Green)]
    [DataRow(AccentColor.Teal)]
    [DataRow(AccentColor.Purple)]
    [DataRow(AccentColor.Pink)]
    [DataRow(AccentColor.Orange)]
    [DataRow(AccentColor.Red)]
    public async Task AccentPreferenceRoundTripsWithoutChangingOtherSettings(AccentColor choice)
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-accent-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(root);
            await store.SaveAsync(new AppSettings { AccentColor = choice, Theme = AppTheme.Dark, DeviceName = "Accent QA", EnrollmentCode = "" });
            var settings = await store.LoadAsync();
            Assert.AreEqual(choice, settings.AccentColor);
            Assert.AreEqual(AppTheme.Dark, settings.Theme);
            Assert.AreEqual("Accent QA", settings.DeviceName);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void PaletteCoversEveryChoiceAndInvalidValuesFallBackToWindows()
    {
        Assert.AreEqual(Enum.GetValues<AccentColor>().Length, AccentPalette.Choices.Select(x => x.Value).Distinct().Count());
        var settings = new AppSettings { AccentColor = (AccentColor)999 };
        settings.Normalize();
        Assert.AreEqual(AccentColor.System, settings.AccentColor);
        Assert.AreEqual(0x123456u, AccentPalette.Resolve((AccentColor)999, false, 0x123456));
    }

    [TestMethod]
    public async Task LegacySettingsDefaultToWindowsAccent()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-accent-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "{\"theme\":\"Dark\",\"deviceName\":\"Legacy QA\",\"enrollmentCode\":\"\"}");
            var settings = await new JsonSettingsStore(root).LoadAsync();
            Assert.AreEqual(AccentColor.System, settings.AccentColor);
            Assert.AreEqual(AppTheme.Dark, settings.Theme);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
