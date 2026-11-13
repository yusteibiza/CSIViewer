namespace CSIViewer.Application;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum FileConflictPolicy
{
    Rename,
    Overwrite,
    Skip
}

public enum HostConsentMode
{
    Ask,
    AcceptAutomatically,
    RejectAutomatically
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string DeviceName { get; set; } = Environment.MachineName;
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string EnrollmentCode { get; set; } = "DEV-1234";
    public string ProtectedEnrollmentCode { get; set; } = "";
    public bool AutoConnect { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public CSIViewer.Core.AccentColor AccentColor { get; set; } = CSIViewer.Core.AccentColor.System;
    public string Language { get; set; } = "es";
    public int ImageQuality { get; set; } = 62;
    public int Fps { get; set; } = 12;
    public int MaxWidth { get; set; } = 1280;
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CSIViewer");
    public int UploadLimitKbps { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool AllowIncomingConnections { get; set; } = true;
    public HostConsentMode HostConsentMode { get; set; } = HostConsentMode.Ask;
    public bool AskBeforeReceivingFiles { get; set; } = true;
    public int MaxConcurrentSessions { get; set; } = 10;
    public FileConflictPolicy FileConflictPolicy { get; set; } = FileConflictPolicy.Rename;
    public bool AutoResumeTransfers { get; set; } = true;
    public bool PreferModernCapture { get; set; } = true;
    public bool PreferH264 { get; set; } = true;
    public bool PreferDirectLan { get; set; } = true;
    public bool RegeneratePasswordOnStartup { get; set; }
    public bool ShowSessionBanner { get; set; } = true;
    public bool StoreConnectionHistory { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 365;
    public int TemporaryPasswordLifetimeMinutes { get; set; } = 60;
    public bool AllowInsecureServer { get; set; }
    public int ConnectTimeoutSeconds { get; set; } = 10;
    public int MessageTimeoutSeconds { get; set; } = 60;
    public int HeartbeatIntervalSeconds { get; set; } = 5;
    public int HeartbeatTimeoutSeconds { get; set; } = 20;
    public int ReconnectMaxRetries { get; set; } = 10;
    public int ReconnectInitialDelaySeconds { get; set; } = 2;
    public int ReconnectMaxDelaySeconds { get; set; } = 30;
    public int ReconnectJitterPercent { get; set; } = 20;
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Information;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        AccentColor = CSIViewer.Core.AccentPalette.Normalize(AccentColor);
        ServerUrl = string.IsNullOrWhiteSpace(ServerUrl) ? "http://localhost:5080" : ServerUrl.Trim();
        DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName.Trim();
        EnrollmentCode = EnrollmentCode?.Trim() ?? "";
        if (EnrollmentCode.Length > 128)
        {
            EnrollmentCode = EnrollmentCode[..128];
        }
        ImageQuality = Math.Clamp(ImageQuality, 20, 95);
        Fps = Math.Clamp(Fps, 1, 120);
        MaxWidth = Math.Clamp(MaxWidth, 640, 2560);
        UploadLimitKbps = Math.Max(0, UploadLimitKbps);
        MaxConcurrentSessions = Math.Clamp(MaxConcurrentSessions, 1, 100);
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 3650);
        TemporaryPasswordLifetimeMinutes = Math.Clamp(TemporaryPasswordLifetimeMinutes, 0, 1440);
        ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, 3, 60);
        MessageTimeoutSeconds = Math.Clamp(MessageTimeoutSeconds, 10, 300);
        HeartbeatIntervalSeconds = Math.Clamp(HeartbeatIntervalSeconds, 2, 120);
        HeartbeatTimeoutSeconds = Math.Clamp(HeartbeatTimeoutSeconds, 5, 600);
        if (HeartbeatTimeoutSeconds < HeartbeatIntervalSeconds + 3)
        {
            HeartbeatTimeoutSeconds = HeartbeatIntervalSeconds + 3;
        }

        ReconnectMaxRetries = Math.Clamp(ReconnectMaxRetries, 0, 100);
        ReconnectInitialDelaySeconds = Math.Clamp(ReconnectInitialDelaySeconds, 1, 60);
        ReconnectMaxDelaySeconds = Math.Clamp(ReconnectMaxDelaySeconds, 1, 600);
        if (ReconnectMaxDelaySeconds < ReconnectInitialDelaySeconds)
        {
            ReconnectMaxDelaySeconds = ReconnectInitialDelaySeconds;
        }

        ReconnectJitterPercent = Math.Clamp(ReconnectJitterPercent, 0, 50);
        if (WindowWidth < 400 || WindowHeight < 300)
        {
            WindowWidth = 0;
            WindowHeight = 0;
            WindowX = 0;
            WindowY = 0;
            WindowMaximized = false;
        }

        if (!Enum.IsDefined(FileConflictPolicy))
        {
            FileConflictPolicy = FileConflictPolicy.Rename;
        }

        if (!Enum.IsDefined(LogLevel))
        {
            LogLevel = AppLogLevel.Information;
        }
        DownloadFolder = string.IsNullOrWhiteSpace(DownloadFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CSIViewer")
            : DownloadFolder;

        try
        {
            var full = Path.GetFullPath(DownloadFolder);
            var leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
            if (leaf.Equals("RemoteDesktop", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("RemoteDesktopClient", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    DownloadFolder = Path.Combine(parent, "CSIViewer");
                }
            }
        }
        catch
        {
            DownloadFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "CSIViewer");
        }

        Language = Language is "es" or "en" ? Language : "es";
    }
}
