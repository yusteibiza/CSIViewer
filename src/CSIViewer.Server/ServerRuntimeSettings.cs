using System.Text.Json.Serialization;
using CSIViewer.Application;

namespace CSIViewer.Server;

public enum ServerTheme
{
    System,
    Light,
    Dark
}

public sealed class ServerRuntimeSettings
{
    public int Port { get; set; } = 5080;
    public bool ListenOnAllInterfaces { get; set; }
    public string EnrollmentCode { get; set; } = global::CSIViewer.Core.Localization.Text("DEV-1234");
    public string ProtectedEnrollmentCode { get; set; } = "";
    public string ServerName { get; set; } = global::CSIViewer.Core.Localization.Text("CSIViewer Servidor");
    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public bool AutoStartWithUi { get; set; }
    public bool StartWithWindows { get; set; }
    public ServerTheme Theme { get; set; } = ServerTheme.System;
    public CSIViewer.Core.AccentColor AccentColor { get; set; } = CSIViewer.Core.AccentColor.System;
    public string Language { get; set; } = "es";
    public bool EnableUpnp { get; set; }
    public int UpnpExternalPort { get; set; }
    public bool UseHttps { get; set; }
    public string CertificateThumbprint { get; set; } = "";
    public bool EnableStunServer { get; set; }
    public int StunPort { get; set; } = 3478;
    public int MaxSessionsPerHost { get; set; } = 10;
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Information;

    /// <summary>URL supplied explicitly on the command line. It is not persisted.</summary>
    [JsonIgnore]
    public string? ExplicitUrl { get; set; }

    public static string GetDefaultDatabasePath()
    {
        var configured = Environment.GetEnvironmentVariable("CSIVIEWER_SERVER_DB");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(ServerDataPaths.DataDirectory, "server.db");
    }

    [JsonIgnore]
    public string Scheme => UseHttps ? "https" : "http";

    [JsonIgnore]
    public string ListenUrl => !string.IsNullOrWhiteSpace(ExplicitUrl)
        ? ExplicitUrl
        : ListenOnAllInterfaces
            ? $"{Scheme}://0.0.0.0:{Port}"
            : $"{Scheme}://localhost:{Port}";

    [JsonIgnore]
    public string ClientUrl => $"{Scheme}://localhost:{Port}";

    [JsonIgnore]
    public string AccessModeText => ListenOnAllInterfaces
        ? global::CSIViewer.Core.Localization.Text("Todas las interfaces de red (LAN/Internet)")
        : global::CSIViewer.Core.Localization.Text("Solo este equipo (localhost)");

    [JsonIgnore]
    public int EffectiveUpnpExternalPort => UpnpExternalPort <= 0 ? Port : UpnpExternalPort;

    public ServerRuntimeSettings Clone() => (ServerRuntimeSettings)MemberwiseClone();

    public void Normalize()
    {
        AccentColor = CSIViewer.Core.AccentPalette.Normalize(AccentColor);
        Language = CSIViewer.Core.Localization.Normalize(Language);
        Port = Math.Clamp(Port, 1024, 65535);
        UpnpExternalPort = UpnpExternalPort <= 0 ? 0 : Math.Clamp(UpnpExternalPort, 1024, 65535);
        StunPort = Math.Clamp(StunPort, 1024, 65535);
        MaxSessionsPerHost = Math.Clamp(MaxSessionsPerHost, 1, 100);
        EnrollmentCode = (EnrollmentCode ?? "").Trim();
        if (EnrollmentCode.Length > 128)
        {
            EnrollmentCode = EnrollmentCode[..128];
        }
        CertificateThumbprint = (CertificateThumbprint ?? "")
            .Replace(" ", "", StringComparison.Ordinal)
            .ToUpperInvariant();
        ServerName = string.IsNullOrWhiteSpace(ServerName) ? global::CSIViewer.Core.Localization.Text("CSIViewer Servidor") : ServerName.Trim();
        DatabasePath = string.IsNullOrWhiteSpace(DatabasePath) ? GetDefaultDatabasePath() : DatabasePath.Trim();
        if (!Enum.IsDefined(Theme))
        {
            Theme = ServerTheme.System;
        }

        if (!Enum.IsDefined(LogLevel))
        {
            LogLevel = AppLogLevel.Information;
        }

        if (WindowWidth < 400 || WindowHeight < 300)
        {
            WindowWidth = 0;
            WindowHeight = 0;
            WindowX = 0;
            WindowY = 0;
            WindowMaximized = false;
        }
    }
}
