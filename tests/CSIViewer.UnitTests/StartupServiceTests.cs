using CSIViewer.Infrastructure;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class StartupServiceTests
{
    [TestMethod]
    public void BuildCommand_QuotesExecutableAndAppendsArguments()
    {
        var command = StartupService.BuildCommand(
            @"C:\Program Files\CSIViewer\CSIViewerServidor.exe",
            "--modo=ui");

        Assert.AreEqual(
            "\"C:\\Program Files\\CSIViewer\\CSIViewerServidor.exe\" --modo=ui",
            command);
    }

    [TestMethod]
    public void BuildCommand_OmitsEmptyArguments()
    {
        var command = StartupService.BuildCommand(@"C:\CSIViewer\CSIViewer.exe", "  ");

        Assert.AreEqual("\"C:\\CSIViewer\\CSIViewer.exe\"", command);
    }

    [TestMethod]
    public void Constructor_RejectsEmptyValueName()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new StartupService(""));
    }

    [TestMethod]
    public void Constructor_UsesProvidedValueName()
    {
        var service = new StartupService("CSIViewerServidor", "--modo=ui");

        Assert.AreEqual("CSIViewerServidor", service.ValueName);
    }

    [TestMethod]
    public void SetEnabled_WritesAndRemovesRunEntry()
    {
        var valueName = "CSIViewerStartupTest_" + Guid.NewGuid().ToString("N");
        var service = new StartupService(valueName, "--modo=ui");
        try
        {
            Assert.IsTrue(service.SetEnabled(true));
            Assert.IsTrue(service.IsEnabled);
            var command = service.RegisteredCommand;
            Assert.IsNotNull(command);
            StringAssert.Contains(command, "--modo=ui");

            Assert.IsTrue(service.SetEnabled(false));
            Assert.IsFalse(service.IsEnabled);
        }
        finally
        {
            service.SetEnabled(false);
        }
    }
}
