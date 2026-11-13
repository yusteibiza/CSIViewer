using CSIViewer.Core;

namespace CSIViewer.Protocol;

/// <summary>
/// Defensive validation for untrusted network payloads. Every value that is
/// converted, allocated or used in a cryptographic primitive should pass here
/// before it reaches business logic.
/// </summary>
public static class MessageValidation
{
    public const int MaxPasswordLength = 128;
    public const int MaxNameLength = 80;
    public const int MaxReasonLength = 512;
    public const int MaxRelayMessageBytes = 2 * 1024 * 1024;
    public const int MaxFileChunkBytes = 2 * 1024 * 1024;

    public static bool IsValidDeviceId(string? value) =>
        value is { Length: 9 } && value.All(char.IsAsciiDigit);

    public static bool IsValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxNameLength &&
        !value.Any(char.IsControl);

    public static bool IsValidPassword(string? value) =>
        value is { Length: >= 1 and <= MaxPasswordLength };

    public static bool IsValidSecret(string? value) =>
        value is { Length: >= 1 and <= MaxPasswordLength };

    public static bool IsValidReason(string? value) =>
        value is null || (value.Length <= MaxReasonLength && !value.Any(char.IsControl));

    public static bool IsValidPermissionMask(SessionPermissions permissions) =>
        (permissions & ~SessionPermissions.All) == 0;

    public static bool IsValidBase64(string? value, int minBytes, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            return decoded.Length >= minBytes && decoded.Length <= maxBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsValidSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static bool IsValidTransferPath(string? value, bool allowEmpty = false) =>
        IsValidRelativePath(value, 1024, allowEmpty);

    public static bool IsValidRelativePath(string? value, int maxLength = 1024, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return allowEmpty;
        }

        if (value.Length > maxLength ||
            value.Any(char.IsControl) ||
            value.Contains('\0'))
        {
            return false;
        }

        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/') ||
            normalized.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 &&
               parts.All(part => part is not "." and not ".." &&
                                 part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    public static bool IsValidInput(InputEventPayload input)
    {
        if (input is null)
        {
            return false;
        }

        return input.Kind switch
        {
            "key" => input.Action is "keyDown" or "keyUp" or "down" or "up" &&
                     input.ScanCode is >= 0 and <= ushort.MaxValue &&
                     input.KeyCode is >= 0 and <= ushort.MaxValue,
            "mouse" => input.Action is "move" or "leftDown" or "leftUp" or "rightDown"
                or "rightUp" or "middleDown" or "middleUp" or "wheel" or "hwheel" &&
                (!input.HasPosition || (input.X is >= 0 and <= 65535 && input.Y is >= 0 and <= 65535)),
            "text" => !string.IsNullOrEmpty(input.Action) && input.Action.Length <= 256,
            _ => false
        };
    }

    public static bool IsValidMonitors(IReadOnlyList<MonitorDescriptor>? monitors) =>
        monitors is { Count: > 0 and <= 32 } &&
        monitors.All(x => x.Index >= 0 && x.Index < 32 &&
                          x.Width is > 0 and <= 32768 &&
                          x.Height is > 0 and <= 32768 &&
                          (x.Name?.Length ?? 0) <= 128);

    public static bool IsValidVideoConfig(VideoConfigPayload config) =>
        config.Width is >= 16 and <= 16384 &&
        config.Height is >= 16 and <= 16384 &&
        config.Fps is >= 1 and <= 240 &&
        config.Quality is >= 1 and <= 100;
}
