using CSIViewer.Infrastructure;

namespace CSIViewer.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class DiagnosticSafetyTests
{
    [TestMethod]
    public void DiagnosticStartupGuardDoesNotCreateOrDeleteRunValues()
    {
        var previous = Environment.GetEnvironmentVariable("CSIVIEWER_PIPELINE_DIAGNOSTICS");
        try
        {
            Environment.SetEnvironmentVariable("CSIVIEWER_PIPELINE_DIAGNOSTICS", "1");
            var startup = new StartupService("CSIViewerDiagnosticGuard_" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(startup.IsEnabled);
            Assert.IsFalse(startup.SetEnabled(true));
            Assert.IsTrue(startup.SetEnabled(false));
            Assert.IsNull(startup.RegisteredCommand);
        }
        finally { Environment.SetEnvironmentVariable("CSIVIEWER_PIPELINE_DIAGNOSTICS", previous); }
    }
}
