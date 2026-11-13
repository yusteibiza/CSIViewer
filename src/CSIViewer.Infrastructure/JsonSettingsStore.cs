using System.Text.Json;
using System.Text.Json.Serialization;
using CSIViewer.Application;

namespace CSIViewer.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string EnrollmentEntropy = "CSIViewer.Settings.Enrollment.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _dataDirectory;

    public JsonSettingsStore()
        : this(null)
    {
    }

    public JsonSettingsStore(string? dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    private string DataDirectory => _dataDirectory ?? AppPaths.DataDirectory;

    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = new AppSettings();
                defaults.Normalize();
                return defaults;
            }

            try
            {
                await using var stream = File.OpenRead(SettingsPath);
                var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken)
                    .ConfigureAwait(false) ?? new AppSettings();
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
                settings.Normalize();
                return settings;
            }
            catch (JsonException)
            {
                var backup = SettingsPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                try
                {
                    File.Copy(SettingsPath, backup, overwrite: true);
                }
                catch
                {
                    // Diagnostic backup is best effort.
                }

                var defaults = new AppSettings();
                defaults.Normalize();
                return defaults;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, persisted, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
