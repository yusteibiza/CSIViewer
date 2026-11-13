namespace CSIViewer.Core;

/// <summary>
/// Centralizes the meaning of session permissions from the local device's
/// point of view. SendFiles/ReceiveFiles are described from the controller's
/// perspective, so the host must interpret them in the opposite direction.
/// </summary>
public static class SessionPermissionPolicy
{
    public static SessionPermissions Sanitize(SessionPermissions permissions) =>
        permissions & SessionPermissions.All;

    public static bool CanSendFiles(bool localIsController, SessionPermissions permissions) =>
        permissions.HasFlag(localIsController
            ? SessionPermissions.SendFiles
            : SessionPermissions.ReceiveFiles);

    public static bool CanReceiveFiles(bool localIsController, SessionPermissions permissions) =>
        permissions.HasFlag(localIsController
            ? SessionPermissions.ReceiveFiles
            : SessionPermissions.SendFiles);

    public static bool CanViewScreen(SessionPermissions permissions) =>
        permissions.HasFlag(SessionPermissions.ViewScreen);

    public static bool CanControl(SessionPermissions permissions) =>
        permissions.HasFlag(SessionPermissions.ControlInput);

    public static bool CanChat(SessionPermissions permissions) =>
        permissions.HasFlag(SessionPermissions.Chat);

    public static bool CanTransferFiles(SessionPermissions permissions) =>
        permissions.HasFlag(SessionPermissions.SendFiles) ||
        permissions.HasFlag(SessionPermissions.ReceiveFiles);
}
