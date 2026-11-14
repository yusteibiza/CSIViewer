using CSIViewer.Core;

namespace CSIViewer.Application;

public static class LoginHostPolicy
{
    public const SessionPermissions AllowedPermissions = SessionPermissions.ViewScreen | SessionPermissions.ControlInput;

    public static SessionPermissions Grant(SessionPermissions requested, bool chatOnly, bool enabled,
        bool isSystem, int workerSession, uint consoleSession)
    {
        if (!enabled || !isSystem || chatOnly || workerSession <= 0 || consoleSession == uint.MaxValue ||
            (uint)workerSession != consoleSession) return SessionPermissions.None;
        var granted = requested & AllowedPermissions;
        if (granted.HasFlag(SessionPermissions.ControlInput)) granted |= SessionPermissions.ViewScreen;
        return granted;
    }
}
