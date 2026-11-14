using System.Xml.Linq;

namespace CSIViewer.UnitTests;

// Static source/resource contracts only. These do not launch WinUI or validate
// visual rendering, the Windows dispatcher, graphics drivers or dialog runtime.
[TestClass]
public sealed class UiSourceSafetyContractTests
{
    private static string Text(string name)
    {
        using var stream = typeof(UiSourceSafetyContractTests).Assembly.GetManifestResourceStream("UiContracts." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ContactPersistenceHandlersObserveFailureAndPreventReentrancy(string app)
    {
        var source = Text(app + ".ContactsCode");
        StringAssert.Contains(source, "private bool _contactBusy;");
        StringAssert.Contains(source, "if (_contactBusy) return;");
        StringAssert.Contains(source, "catch (Exception ex) { ShowContactError(ex); }");
        StringAssert.Contains(source, "finally { _contactBusy = false; }");
        StringAssert.Contains(source, "ContactsStatusBar.Severity = InfoBarSeverity.Error;");
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void MonitorRequestsObserveFailuresAndJpegWriterHasExceptionSafeLifetime(string app)
    {
        var source = Text(app + ".SessionCode");
        StringAssert.Contains(source, "using var writer = new DataWriter(stream);");
        StringAssert.Contains(source, "private async Task RequestMonitorsSafelyAsync()");
        StringAssert.Contains(source, "catch (Exception ex) { ShowRemoteOperationError(ex); }");
    }

    [TestMethod]
    public void CaptureReleasesBothOwnedAbiReferences()
    {
        var source = Text("media.WgcCode");
        StringAssert.Contains(source, "if (inspectablePtr != IntPtr.Zero) Marshal.Release(inspectablePtr);");
        StringAssert.Contains(source, "if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);");
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ThemeDictionariesHaveUniqueKeysAndSystemHighContrastBrushes(string app)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Parse(Text(app + ".Colors"));
        foreach (var theme in new[] { "Default", "Light", "Dark", "HighContrast" })
        {
            var dictionary = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == theme);
            var keys = dictionary.Elements().Select(element => (string?)element.Attribute(x + "Key")).ToArray();
            Assert.AreEqual(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            if (theme == "HighContrast")
                foreach (var brush in dictionary.Elements().Where(element => element.Name.LocalName == "SolidColorBrush"))
                    StringAssert.StartsWith((string)brush.Attribute("Color")!, "{ThemeResource SystemColor");
        }
    }
}
