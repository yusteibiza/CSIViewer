using CSIViewer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace CSIViewer.Presentation;

/// <summary>Compiled separately into each UI executable; never changes Windows settings.</summary>
public static class AccentAppearance
{
    private static readonly UISettings SystemSettings = new();
    private static ResourceDictionary? _resources;
    private static FrameworkElement? _root;
    private static Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private static AccentColor _current;
    private static bool _applying;
    public static event EventHandler? Changed;

    private static readonly string[] FillKeys = ["BrandBrush", "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush",
        "AccentFillColorTertiaryBrush", "AccentFillColorSelectedTextBackgroundBrush", "AccentButtonBackground",
        "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed", "SystemControlHighlightAccentBrush",
        "SystemControlForegroundAccentBrush", "SystemControlBackgroundAccentBrush"];
    private static readonly string[] TextKeys = ["AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush",
        "AccentTextFillColorTertiaryBrush", "HyperlinkForeground", "HyperlinkForegroundPointerOver", "HyperlinkForegroundPressed"];
    // Keep Fluent's native TextOnAccent brushes: switches/check glyphs use white
    // in Light and black in Dark. Only our button aliases need contrast pairing.
    private static readonly string[] OnAccentKeys = ["AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed"];
    private static readonly string[] ColorKeys = ["SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2",
        "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"];
    private static readonly string[] NativeAccentKeys = new[]
    {
        "SliderThumbBackground", "SliderTrackValueFill", "RadioButtonOuterEllipseCheckedStroke",
        "RadioButtonOuterEllipseCheckedFill", "CheckBoxCheckBackgroundStrokeChecked",
        "CheckBoxCheckBackgroundStrokeIndeterminate", "CheckBoxCheckBackgroundFillChecked",
        "CheckBoxCheckBackgroundFillIndeterminate", "ToggleSwitchFillOn", "ToggleSwitchStrokeOn",
        "ToggleButtonBackgroundChecked"
    }.SelectMany(prefix => new[] { prefix, prefix + "PointerOver", prefix + "Pressed" })
        .Concat(new[] { "NavigationViewSelectionIndicatorForeground", "TextControlBorderBrushFocused",
            "ListViewItemSelectionIndicatorBrush", "ListViewItemSelectionIndicatorPointerOverBrush",
            "ListViewItemSelectionIndicatorPressedBrush" }).ToArray();

    public static void Apply(AccentColor choice, FrameworkElement? root)
    {
        if (root is null || _applying) return;
        _applying = true;
        try
        {
            _root = root;
            _dispatcher = root.DispatcherQueue;
            _current = AccentPalette.Normalize(choice);
            if (_resources is null)
            {
                _resources = new ResourceDictionary();
                foreach (var theme in new[] { "Light", "Dark", "Default" })
                    _resources.ThemeDictionaries[theme] = new ResourceDictionary();
                _resources.ThemeDictionaries["HighContrast"] = CreateHighContrast();
                Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries.Add(_resources);
                SystemSettings.ColorValuesChanged += (_, _) => _dispatcher?.TryEnqueue(() => Apply(_current, _root));
            }
            foreach (var theme in new[] { "Light", "Dark", "Default" })
            {
                var dark = theme != "Light";
                var system = SystemSettings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
                var fill = AccentPalette.Resolve(_current, dark, Rgb(system));
                var dictionary = (ResourceDictionary)_resources.ThemeDictionaries[theme];
                foreach (var key in ColorKeys) dictionary[key] = ToColor(fill);
                foreach (var key in FillKeys.Concat(TextKeys).Concat(NativeAccentKeys)) SetBrush(dictionary, key, fill);
                foreach (var key in OnAccentKeys) SetBrush(dictionary, key, AccentPalette.Foreground(fill));
                SetBrush(dictionary, "BrandSoftBrush", Mix(fill, dark ? 0x202020u : 0xFFFFFFu, 0.15));
            }
            // ThemeResource consumers and Fluent's cached brush aliases refresh
            // in the same dispatcher turn, without changing the selected theme.
            var requested = root.RequestedTheme;
            root.RequestedTheme = root.ActualTheme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
            root.RequestedTheme = requested;
            Changed?.Invoke(null, EventArgs.Empty);
        }
        finally { _applying = false; }
    }

    private static ResourceDictionary CreateHighContrast()
    {
        var entries = new List<string>();
        foreach (var key in FillKeys.Concat(NativeAccentKeys)) entries.Add($"<SolidColorBrush x:Key=\"{key}\" Color=\"{{ThemeResource SystemColorHighlightColor}}\" />");
        foreach (var key in TextKeys) entries.Add($"<SolidColorBrush x:Key=\"{key}\" Color=\"{{ThemeResource SystemColorWindowTextColor}}\" />");
        foreach (var key in OnAccentKeys) entries.Add($"<SolidColorBrush x:Key=\"{key}\" Color=\"{{ThemeResource SystemColorHighlightTextColor}}\" />");
        entries.Add("<SolidColorBrush x:Key=\"BrandSoftBrush\" Color=\"{ThemeResource SystemColorButtonFaceColor}\" />");
        foreach (var key in ColorKeys) entries.Add($"<StaticResource x:Key=\"{key}\" ResourceKey=\"SystemColorHighlightColor\" />");
        return (ResourceDictionary)XamlReader.Load("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + string.Join("", entries) + "</ResourceDictionary>");
    }

    private static void SetBrush(ResourceDictionary dictionary, string key, uint rgb)
    {
        // Resource lookup can return a Fluent StaticResource alias sharing its
        // brush with native control glyphs. Never mutate that shared instance.
        dictionary[key] = new SolidColorBrush(ToColor(rgb));
    }
    private static uint Mix(uint accent, uint background, double weight)
    {
        uint Channel(int shift) => (uint)Math.Round(((accent >> shift) & 255) * weight + ((background >> shift) & 255) * (1 - weight));
        return Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
    private static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);
    private static Color ToColor(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
