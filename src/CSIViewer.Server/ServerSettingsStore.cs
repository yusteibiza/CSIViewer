using System.Text.Json;
using System.Text.Json.Serialization;
using CSIViewer.Infrastructure;

namespace CSIViewer.Server;

public static class ServerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string EnrollmentEntropy = "CSIViewer.Server.Enrollment.v1";
    private static readonly string DataRoot = ServerDataPaths.DataDirectory;

    public static string DataDirectory => DataRoot;

    public static string SettingsPath => Path.Combine(DataDirectory, "serversettings.json");

    public static ServerRuntimeSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new ServerRuntimeSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ServerRuntimeSettings>(json, JsonOptions)
                           ?? new ServerRuntimeSettings();
            if (string.IsNullOrWhiteSpace(settings.EnrollmentCode) &&
                !string.IsNullOrWhiteSpace(settings.ProtectedEnrollmentCode))
            {
                var unprotected = SecretProtector.Unprotect(
                    settings.ProtectedEnrollmentCode,
                    EnrollmentEntropy);
                if (!string.IsNullOrWhiteSpace(unprotected))
                {
                    settings.EnrollmentCode = unprotected;
                }
            }

            settings.ProtectedEnrollmentCode = "";
            settings.DatabasePath = ServerDataPaths.MigrateDatabasePath(settings.DatabasePath);
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fail closed: a corrupt settings file must not silently fall
            // back to the well-known development enrollment code.
            return new ServerRuntimeSettings { EnrollmentCode = "" };
        }
    }

    public static void Save(ServerRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        var persisted = settings.Clone();
        if (!string.IsNullOrWhiteSpace(persisted.EnrollmentCode))
        {
            persisted.ProtectedEnrollmentCode = SecretProtector.Protect(
                persisted.EnrollmentCode,
                EnrollmentEntropy);
            persisted.EnrollmentCode = "";
        }

        Directory.CreateDirectory(DataDirectory);
        var temp = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        File.WriteAllText(temp, json);
        File.Move(temp, SettingsPath, overwrite: true);
    }
}