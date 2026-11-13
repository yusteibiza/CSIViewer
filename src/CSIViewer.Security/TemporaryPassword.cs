using System.Security.Cryptography;

namespace CSIViewer.Security;

public sealed record TemporaryPassword(string Raw, string Display)
{
    public override string ToString() => Display;
}

public static class TemporaryPasswordGenerator
{
    // Sin caracteres ambiguos (0/O, 1/I/L).
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int Length = 12;

    public static TemporaryPassword Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var raw = new string(chars);
        var display = string.Join('-', raw.Chunk(4).Select(chunk => new string(chunk)));
        // The raw value must remain available for display/copy; it is kept in
        // memory only and never written to disk.
        return new TemporaryPassword(raw, display);
    }

    /// <summary>
    /// Accepts a temporary password typed exactly as displayed (groups separated
    /// by hyphens, optionally with spaces or lowercase). The stored hash uses
    /// the canonical uppercase value without separators.
    /// </summary>
    public static string NormalizeInput(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character == '-' || char.IsWhiteSpace(character))
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    public static string GenerateToken(int bytes = 32)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
