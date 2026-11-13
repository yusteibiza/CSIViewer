using CSIViewer.Application;
using CSIViewer.Infrastructure;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class JsonSettingsStoreSecurityTests
{
    [TestMethod]
    public async Task EnrollmentCode_IsProtectedAtRest()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "csiviewer-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonSettingsStore(directory);
            var settings = new AppSettings
            {
                EnrollmentCode = "SUPER-SECRET-ENROLLMENT-CODE",
                DeviceName = "Test"
            };

            await store.SaveAsync(settings);

            var path = Path.Combine(directory, "settings.json");
            var json = await File.ReadAllTextAsync(path);
            Assert.IsFalse(json.Contains("SUPER-SECRET-ENROLLMENT-CODE", StringComparison.Ordinal));
            Assert.IsTrue(json.Contains("protectedEnrollmentCode", StringComparison.OrdinalIgnoreCase));

            var loaded = await store.LoadAsync();
            Assert.AreEqual("SUPER-SECRET-ENROLLMENT-CODE", loaded.EnrollmentCode);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
