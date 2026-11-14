using System.Xml.Linq;
using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class PresentationLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Loc = "using:CSIViewer.Presentation";

    private static XDocument Read(string app, string page)
    {
        using var stream = typeof(PresentationLayoutTests).Assembly.GetManifestResourceStream($"UiContracts.{app}.{page}")!;
        return XDocument.Load(stream);
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ContactsPrefillSavedDeviceCredentialsAndNavigationClearsPreviousSecret(string app)
    {
        string Code(string name)
        {
            using var stream = typeof(PresentationLayoutTests).Assembly.GetManifestResourceStream($"UiContracts.{app}.{name}")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var contacts = Code("ContactsCode");
        StringAssert.Contains(contacts, "SecretProtector.Unprotect(device.ProtectedPassword, \"CSIViewer.DevicePassword.v1\")");
        StringAssert.Contains(contacts, "RemoteControlPage.PendingPassword = password;");
        StringAssert.Contains(contacts, "RemoteControlPage.PendingAutoConnect = !string.IsNullOrWhiteSpace(password);");
        StringAssert.Contains(Code("SessionCode"), "PasswordBox.Password = PendingPassword ?? \"\";");
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void WindowGapsHaveOpaqueThemeBackgroundAndLinkedDevicesHaveDistinctColors(string app)
    {
        var root = Read(app, "Main").Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == "RootGrid");
        Assert.AreEqual("{ThemeResource PageBackgroundBrush}", (string?)root.Attribute("Background"));
        var colors = Read(app, "Colors");
        foreach (var theme in new[] { "Default", "Light", "Dark", "HighContrast" })
        {
            var dictionary = colors.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == theme);
            foreach (var key in new[] { "LinkedDeviceBackgroundBrush", "LinkedDeviceForegroundBrush" })
                Assert.IsNotNull(dictionary.Elements().SingleOrDefault(x => (string?)x.Attribute(Xaml + "Key") == key));
        }
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void NavigationPaneUsesNeutralThemeBackgroundWithoutOverridingHighContrast(string app)
    {
        var document = Read(app, "Colors");
        foreach (var key in new[] { "NavigationViewDefaultPaneBackground", "NavigationViewExpandedPaneBackground" })
        {
            foreach (var (theme, color) in new[] { ("Light", "#F3F3F3"), ("Dark", "#202020") })
            {
                var dictionary = document.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == theme);
                Assert.AreEqual(color, (string?)dictionary.Elements().Single(x => (string?)x.Attribute(Xaml + "Key") == key).Attribute("Color"));
            }
            var contrast = document.Descendants().Single(x => (string?)x.Attribute(Xaml + "Key") == "HighContrast");
            Assert.IsFalse(contrast.Elements().Any(x => (string?)x.Attribute(Xaml + "Key") == key));
        }
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ContactRowsLabelDevicesWithoutStackingThemBelowActionButtons(string app)
    {
        var document = Read(app, "Contacts");
        var row = document.Descendants().Single(x => x.Attribute(Loc + "RowHover.Brush") is not null);
        Assert.IsNull(row.Element(row.Name.Namespace + "Grid.RowDefinitions"));
        Assert.AreEqual("10", (string?)row.Attribute("Padding"));
        var label = row.Descendants().Single(x => (string?)x.Attribute(Loc + "Translate.Text") == "Equipos vinculados");
        var devices = row.Descendants().Single(x => (string?)x.Attribute("ItemsSource") == "{Binding Devices}");
        Assert.AreSame(label.Parent!.Parent, devices.Parent);
        Assert.AreEqual("1", (string?)devices.Parent!.Attribute("Grid.Column"));
        Assert.AreEqual("0,6,0,4", (string?)label.Parent.Attribute("Margin"));
        Assert.AreEqual("4", (string?)devices.Parent.Attribute("Spacing"));
        Assert.AreEqual("{ThemeResource LinkedDeviceBackgroundBrush}", (string?)label.Parent.Attribute("Background"));
        Assert.AreEqual("{ThemeResource LinkedDeviceForegroundBrush}", (string?)label.Attribute("Foreground"));
        var device = devices.Descendants().Single(x => x.Name.LocalName == "Button");
        Assert.IsNull(device.Attribute("Background"));
        Assert.IsNull(device.Attribute("Foreground"));
        var edit = row.Descendants().Single(x => (string?)x.Attribute("AutomationProperties.AutomationId") == "EditContactButton");
        Assert.AreSame(row, edit.Parent!.Parent);
        Assert.AreEqual("28", (string?)edit.Attribute("MinHeight"));
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ContactsUseFullWidthThemeAwareHoverWithoutSelection(string app)
    {
        var document = Read(app, "Contacts");
        var list = document.Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == "ContactsList");
        Assert.AreEqual("None", (string?)list.Attribute("SelectionMode"));
        var row = list.Descendants().Single(x => x.Attribute(Loc + "RowHover.Brush") is not null);
        Assert.AreEqual("{ThemeResource SubtleFillColorSecondaryBrush}", (string?)row.Attribute(Loc + "RowHover.Brush"));
        Assert.AreEqual("Transparent", (string?)row.Attribute("Background"));
        Assert.IsTrue(list.Descendants().Any(x => x.Name.LocalName == "Setter" &&
            (string?)x.Attribute("Property") == "HorizontalContentAlignment" && (string?)x.Attribute("Value") == "Stretch"));
        var edit = list.Descendants().Single(x => (string?)x.Attribute("AutomationProperties.AutomationId") == "EditContactButton");
        var delete = list.Descendants().Single(x => (string?)x.Attribute("AutomationProperties.AutomationId") == "DeleteContactButton");
        Assert.AreSame(edit.Parent, delete.Parent);
        Assert.AreEqual("Stretch", (string?)edit.Attribute("HorizontalAlignment"));
        Assert.AreEqual("Stretch", (string?)delete.Attribute("HorizontalAlignment"));
        Assert.AreEqual(edit.Attribute("MinWidth")!.Value, delete.Attribute("MinWidth")!.Value);
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ConnectionPlaceholderIsCenteredAndProgressStartsInactive(string app)
    {
        var document = Read(app, "Session");
        var content = document.Descendants().Single(x => (string?)x.Attribute("AutomationProperties.AutomationId") == "ConnectionEmptyState");
        Assert.AreEqual("Center", (string?)content.Attribute("HorizontalAlignment"));
        Assert.AreEqual("Center", (string?)content.Attribute("VerticalAlignment"));
        Assert.AreEqual("Center", (string?)content.Elements().Single(x => x.Name.LocalName == "FontIcon").Attribute("HorizontalAlignment"));
        var spinner = content.Elements().Single(x => x.Name.LocalName == "ProgressRing");
        Assert.AreEqual("False", (string?)spinner.Attribute("IsActive"));
        Assert.AreEqual("Collapsed", (string?)spinner.Attribute("Visibility"));
        foreach (var text in content.Elements().Where(x => x.Name.LocalName == "TextBlock"))
        {
            Assert.AreEqual("Center", (string?)text.Attribute("TextAlignment"));
            Assert.AreEqual("Wrap", (string?)text.Attribute("TextWrapping"));
        }
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void FileNavigationUsesAccessibleIconsButRetainsDrivesText(string app)
    {
        var document = Read(app, "Files");
        foreach (var prefix in new[] { "Local", "Remote" })
        foreach (var command in new[] { "Up", "Go", "Refresh" })
        {
            var button = document.Descendants().Single(x => (string?)x.Attribute("AutomationProperties.AutomationId") == prefix + "File" + command);
            Assert.IsTrue(button.Elements().Any(x => x.Name.LocalName == "FontIcon"));
            Assert.IsNull(button.Attribute("Content"));
            Assert.IsNull(button.Attribute("ContentTemplate"));
            var name = (string)button.Attribute("AutomationProperties.Name")!;
            Assert.AreEqual(name, (string?)button.Attribute("ToolTipService.ToolTip"));
            Assert.AreEqual(name, (string?)button.Attribute(Loc + "Translate.AutomationName"));
            Assert.IsTrue(Localization.Catalog("es").ContainsKey(name));
        }
        Assert.AreEqual(2, document.Descendants().Count(x => x.Name.LocalName == "Button" && (string?)x.Attribute("Content") == "Unidades"));
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void SessionFeatureCheckboxesDoNotReserveNative120PixelMinimum(string app)
    {
        var document = Read(app, "Session");
        foreach (var name in new[] { "SessionAudioBox", "SessionClipboardBox" })
        {
            var box = document.Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == name);
            Assert.AreEqual("0", (string?)box.Attribute("MinWidth"));
            Assert.AreEqual("8,0,0,0", (string?)box.Attribute("Padding"));
            Assert.AreEqual("Center", (string?)box.Attribute("VerticalAlignment"));
            Assert.AreEqual("Center", (string?)box.Attribute("VerticalContentAlignment"));
        }
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void FileCountsUseFullPaginationRowHeightForVerticalCentering(string app)
    {
        var document = Read(app, "Files");
        foreach (var name in new[] { "LocalCountText", "RemoteCountText" })
        {
            var count = document.Descendants().Single(x => (string?)x.Attribute(Xaml + "Name") == name);
            Assert.AreEqual("Center", (string?)count.Attribute("VerticalAlignment"));
            Assert.AreEqual("True", (string?)count.Parent!.Attribute("AlignItemsVertically"));
        }
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void ChatActionsRespectApplicationRoleAndPreserveIndentedLeftAlignedBubbles(string app)
    {
        var document = Read(app, "Chat");
        foreach (var name in new[] { "ChatDeviceCombo", "DeleteChatButton", "ExportChatButton", "EmojiCategoryCombo" })
            Assert.IsNotNull(document.Descendants().SingleOrDefault(x => (string?)x.Attribute("AutomationProperties.AutomationId") == name));
        var selector = document.Descendants().Single(x => (string?)x.Attribute("AutomationProperties.AutomationId") == "ChatDeviceCombo");
        Assert.AreEqual(app == "host" ? "Collapsed" : null, (string?)selector.Parent!.Attribute("Visibility"));
        var sent = document.Descendants().Single(x => (string?)x.Attribute("Visibility") == "{Binding SentVisibility}");
        Assert.AreEqual("28,0,0,0", (string?)sent.Attribute("Margin"));
        Assert.AreEqual("Left", (string?)sent.Attribute("HorizontalAlignment"));
        var received = document.Descendants().Single(x => (string?)x.Attribute("Visibility") == "{Binding ReceivedVisibility}");
        Assert.AreEqual("Left", (string?)received.Attribute("HorizontalAlignment"));
    }

    [TestMethod]
    [DataRow("viewer")]
    [DataRow("host")]
    public void DeviceListBadgeAndActionsShareVerticalCenterAndThemeAwareHover(string app)
    {
        var document = Read(app, "Devices");
        var badge = document.Descendants().Single(x => (string?)x.Attribute("Text") == "{Binding ListName}");
        var group = badge.Parent!.Parent!;
        Assert.AreEqual("2", (string?)group.Attribute("Grid.Column"));
        Assert.AreEqual("Center", (string?)group.Attribute("VerticalAlignment"));
        var row = group.Parent!;
        var actions = row.Elements().Single(x => x.Name.LocalName == "WrapPanel");
        Assert.AreEqual("3", (string?)actions.Attribute("Grid.Column"));
        Assert.AreEqual("Center", (string?)actions.Attribute("VerticalAlignment"));
        Assert.AreEqual("{ThemeResource SubtleFillColorSecondaryBrush}", (string?)row.Attribute(Loc + "RowHover.Brush"));
        Assert.AreEqual("Transparent", (string?)row.Attribute("Background"));
    }
}
