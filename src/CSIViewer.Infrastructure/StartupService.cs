using Microsoft.Win32;

namespace CSIViewer.Infrastructure;

/// <summary>
/// Registers a per-user Windows startup entry (HKCU Run) so the application
/// starts when the user signs in. The default instance is the client; other
/// applications such as the server can use a different value name and
/// arguments.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;
    private readonly string? _arguments;
    private readonly string? _executablePath;

    public StartupService()
        : this("CSIViewer", null)
    {
    }

    public StartupService(string valueName, string? arguments = null, string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(valueName))
        {
            throw new ArgumentException("El nombre del valor de inicio no puede estar vacío.", nameof(valueName));
        }

        _valueName = valueName;
        _arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        _executablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
    }

    public string ValueName => _valueName;

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(_valueName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    public string? RegisteredCommand
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(_valueName) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var executable = _executablePath ?? Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable) ||
                    string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    var local = Path.Combine(AppContext.BaseDirectory, _valueName + ".exe");
                    if (File.Exists(local))
                    {
                        executable = local;
                    }
                }

                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    return false;
                }

                key.SetValue(
                    _valueName,
                    BuildCommand(executable, _arguments),
                    RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(_valueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the command line stored in the Run key. The executable is always
    /// quoted; arguments are appended only when present.
    /// </summary>
    public static string BuildCommand(string executable, string? arguments = null)
    {
        var command = $"\"{executable}\"";
        return string.IsNullOrWhiteSpace(arguments)
            ? command
            : command + " " + arguments.Trim();
    }
}
