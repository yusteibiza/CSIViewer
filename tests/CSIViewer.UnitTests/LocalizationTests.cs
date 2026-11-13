using System.Text.RegularExpressions;
using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class LocalizationTests
{
    [TestMethod]
    [DataRow("en")]
    [DataRow("fr")]
    [DataRow("ca")]
    public void EveryCatalogHasAllSpanishKeysAndPreservesFormatArguments(string language)
    {
        var spanish = Localization.Catalog("es");
        var translated = Localization.Catalog(language);
        Assert.IsTrue(spanish.Count > 800);
        Assert.AreEqual(spanish.Count, translated.Count);
        foreach (var pair in spanish)
        {
            Assert.IsTrue(translated.TryGetValue(pair.Key, out var value), "Missing resource: " + pair.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(value));
            var original = Regex.Matches(pair.Key, @"\{\d+[^}]*\}").Select(x => x.Value).Order(StringComparer.Ordinal);
            var actual = Regex.Matches(value, @"\{\d+[^}]*\}").Select(x => x.Value).Order(StringComparer.Ordinal);
            CollectionAssert.AreEqual(original.ToArray(), actual.ToArray());
            Assert.IsFalse(value.Contains("ZXKEEP", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow("es", "Configuración")]
    [DataRow("en", "Settings")]
    [DataRow("fr", "Paramètres")]
    [DataRow("ca", "Configuració")]
    public void LanguageCanChangeWithoutChangingUserText(string language, string settings)
    {
        var previous = Localization.Language;
        try
        {
            Localization.Use(language);
            Assert.AreEqual(settings, Localization.Text("Configuración"));
            Assert.AreEqual("My own device 123456789", Localization.Text("My own device 123456789"));
            Assert.AreEqual("file.offer", Localization.Text("file.offer"));
        }
        finally { Localization.Use(previous); }
    }

    [TestMethod]
    public void UnsupportedLanguageFallsBackToSpanish()
    {
        Assert.AreEqual("es", Localization.Normalize("unknown"));
        Assert.AreEqual("es", Localization.Normalize(null));
        Assert.AreEqual(4, Localization.Languages.Count);
    }

    [TestMethod]
    [DataRow("es")]
    [DataRow("en")]
    [DataRow("fr")]
    [DataRow("ca")]
    public void SettingsKeepEverySupportedLanguage(string code)
    {
        var settings = new CSIViewer.Application.AppSettings { Language = code };
        settings.Normalize();
        Assert.AreEqual(code, settings.Language);
    }

    [TestMethod]
    [DataRow("es")]
    [DataRow("en")]
    [DataRow("fr")]
    [DataRow("ca")]
    public void DeviceIdentifierIsNotTranslatedAsAnIdentityDocument(string code)
    {
        Assert.AreEqual("ID —", Localization.Catalog(code)["ID —"]);
    }
}
