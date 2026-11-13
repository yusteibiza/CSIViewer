namespace CSIViewer.Core;

public enum AccentColor { System, Blue, Green, Teal, Purple, Pink, Orange, Red }

public sealed record AccentChoice(AccentColor Value, string Name, string PreviewColor)
{
    public override string ToString() => Name;
}

public static class AccentPalette
{
    public static IReadOnlyList<AccentChoice> Choices { get; } = Array.AsReadOnly(new[]
    {
        new AccentChoice(AccentColor.System, "Color de Windows", "#808080"),
        new AccentChoice(AccentColor.Blue, "Azul", "#0067C0"),
        new AccentChoice(AccentColor.Green, "Verde", "#107C10"),
        new AccentChoice(AccentColor.Teal, "Turquesa", "#007C83"),
        new AccentChoice(AccentColor.Purple, "Violeta", "#744DA9"),
        new AccentChoice(AccentColor.Pink, "Rosa", "#B40066"),
        new AccentChoice(AccentColor.Orange, "Naranja", "#BC4B00"),
        new AccentChoice(AccentColor.Red, "Rojo", "#C42B1C")
    });

    public static AccentColor Normalize(AccentColor value) => Enum.IsDefined(value) ? value : AccentColor.System;

    public static uint Resolve(AccentColor value, bool dark, uint systemColor) => Normalize(value) switch
    {
        AccentColor.Blue => dark ? 0x60CDFFu : 0x0067C0u,
        AccentColor.Green => dark ? 0x6CCB5Fu : 0x107C10u,
        AccentColor.Teal => dark ? 0x5CD0CCu : 0x007C83u,
        AccentColor.Purple => dark ? 0xC6A0F6u : 0x744DA9u,
        AccentColor.Pink => dark ? 0xF69AC5u : 0xB40066u,
        AccentColor.Orange => dark ? 0xFFB579u : 0xBC4B00u,
        AccentColor.Red => dark ? 0xFF9999u : 0xC42B1Cu,
        _ => systemColor & 0xFFFFFF
    };

    public static uint Foreground(uint background) => Contrast(background, 0xFFFFFF) >= Contrast(background, 0) ? 0xFFFFFFu : 0u;

    public static double Contrast(uint first, uint second)
    {
        static double Luminance(uint rgb)
        {
            static double Linear(uint value)
            {
                var c = value / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear((rgb >> 16) & 255) + 0.7152 * Linear((rgb >> 8) & 255) + 0.0722 * Linear(rgb & 255);
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
