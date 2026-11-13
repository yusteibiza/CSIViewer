using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Networking;

namespace CSIViewer.Services;

public sealed class DiagnosticsService
{
    private readonly ServerApiClient _api;
    private readonly AppState _state;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(ServerApiClient api, AppState state, ILogger<DiagnosticsService> logger)
    {
        _api = api;
        _state = state;
        _logger = logger;
    }

    public async Task<string> TestServerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ServerEndpointSecurity.EnsureAllowed(
                _state.Settings.ServerUrl,
                _state.Settings.AllowInsecureServer);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        var ok = await _api.CheckHealthAsync(_state.Settings.ServerUrl, cancellationToken).ConfigureAwait(false);
        return ok
            ? $"Servidor accesible: {_state.Settings.ServerUrl}"
            : $"No se pudo contactar con {_state.Settings.ServerUrl}";
    }

    public async Task<string> TestStunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await StunClient.QueryAsync("stun.l.google.com", 19302, cancellationToken)
                .ConfigureAwait(false);
            return $"STUN {result.Server} → {result.PublicEndPoint}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "STUN diagnostic failed");
            return "STUN no disponible (UDP bloqueado o sin Internet).";
        }
    }

    /// <summary>
    /// Creates a ZIP bundle with logs and a redacted diagnostics summary. It
    /// never includes passwords, tokens, enrollment codes or private keys.
    /// </summary>
    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CSIViewerDiagnostics-" + Guid.NewGuid().ToString("N"));
        var logsDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(logsDirectory);
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDirectory, "csiviewer-*.log*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(logsDirectory, Path.GetFileName(file)), overwrite: true);
            }

            var summary = new StringBuilder();
            summary.AppendLine("CSIViewer - diagnóstico");
            summary.AppendLine($"Generado: {DateTimeOffset.Now:O}");
            summary.AppendLine($"Versión: {typeof(DiagnosticsService).Assembly.GetName().Version}");
            summary.AppendLine($"OS: {Environment.OSVersion}");
            summary.AppendLine($"64-bit: {Environment.Is64BitProcess}");
            summary.AppendLine($"Procesadores: {Environment.ProcessorCount}");
            summary.AppendLine($"Dispositivo: {_state.DeviceName}");
            summary.AppendLine($"DeviceId: {_state.DeviceId}");
            summary.AppendLine($"Servidor: {SensitiveDataRedactor.Redact(_state.Settings.ServerUrl)}");
            summary.AppendLine($"Estado: {_state.ConnectionState} / {_state.ServerStatus}");
            summary.AppendLine($"Sesiones activas: {_state.ActiveSessionCount}");
            summary.AppendLine($"LogLevel: {_state.Settings.LogLevel}");
            summary.AppendLine($"FPS: {_state.Settings.Fps}; Calidad: {_state.Settings.ImageQuality}; Ancho: {_state.Settings.MaxWidth}");
            summary.AppendLine($"H264: {_state.Settings.PreferH264}; WGC: {_state.Settings.PreferModernCapture}");
            summary.AppendLine($"Último error: {SensitiveDataRedactor.Redact(_state.LastError ?? "(ninguno)")}");
            summary.AppendLine();
            summary.AppendLine("No se incluyen contraseñas, tokens, códigos de inscripción ni claves privadas.");
            await File.WriteAllTextAsync(
                    Path.Combine(root, "diagnostics.txt"),
                    summary.ToString(),
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);

            var crashLog = Path.Combine(AppPaths.DataDirectory, "ui-crash.log");
            if (File.Exists(crashLog))
            {
                var crashText = SensitiveDataRedactor.Redact(File.ReadAllText(crashLog));
                await File.WriteAllTextAsync(
                        Path.Combine(root, "ui-crash.log"),
                        crashText,
                        Encoding.UTF8,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var zipPath = Path.Combine(
                Path.GetTempPath(),
                $"CSIViewer-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(root, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            _logger.LogInformation(
                LogEvents.DiagnosticsExported,
                "Diagnostic bundle created at {Path}",
                zipPath);
            return zipPath;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // The temporary directory is best effort.
            }
        }
    }
}
