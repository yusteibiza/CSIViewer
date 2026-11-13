using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace CSIViewer.Core;

public sealed record LanguageChoice(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

public static class Localization
{
    private static string _language = "es";
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new();
    private static readonly ConcurrentDictionary<string, string> FormattedSources = new(StringComparer.Ordinal);
    public static IReadOnlyList<LanguageChoice> Languages { get; } = Array.AsReadOnly(new[]
    {
        new LanguageChoice("es", "Español"), new LanguageChoice("en", "English"),
        new LanguageChoice("fr", "Français"), new LanguageChoice("ca", "Català")
    });
    public static string Language => _language;
    public static event EventHandler? Changed;
    public static string Normalize(string? code) => code is "en" or "fr" or "ca" ? code : "es";

    public static bool Use(string? code)
    {
        var normalized = Normalize(code);
        if (_language == normalized) return false;
        _language = normalized;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    public static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs.GetOrAdd(Normalize(language), code =>
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream($"CSIViewer.Core.Resources.{code}.json");
        return stream is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    });

    public static string Text(string? source)
    {
        if (source is null) return "";
        return _language != "es" && Catalog(_language).TryGetValue(source, out var value) ? value : source;
    }

    public static string Source(string value)
    {
        if (FormattedSources.TryGetValue(value, out var source)) return source;
        if (_language == "es") return value;
        return Catalog(_language).FirstOrDefault(x => x.Value == value).Key ?? value;
    }

    public static string Format(string source, params object?[] arguments)
    {
        var result = string.Format(CultureInfo.CurrentCulture, Text(source), arguments);
        if (_language != "es")
        {
            if (FormattedSources.Count > 512) FormattedSources.Clear();
            FormattedSources[result] = string.Format(CultureInfo.CurrentCulture, source, arguments);
        }
        return result;
    }
}
