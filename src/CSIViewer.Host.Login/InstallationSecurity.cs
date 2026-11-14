using System.Security.AccessControl;
using System.Security.Principal;
using CSIViewer.Platform;

namespace CSIViewer.Host.Login;

internal static class InstallationSecurity
{
    public static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Run elevated as administrator.");
    }

    public static void RequireSystem(bool interactive)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        if (!identity.IsSystem || (interactive ? session <= 0 : session != 0))
            throw new UnauthorizedAccessException("The broker requires SYSTEM/session 0; the worker requires SYSTEM in the interactive session.");
    }

    public static void ValidateInstallation()
    {
        if (!string.Equals(Path.GetDirectoryName(Environment.ProcessPath), LoginConfiguration.InstallDirectory, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Run only from the protected default CSIViewer Host installation.");
        ProtectedInstallation.ValidateProgramFilesParents(Environment.ProcessPath!);
        ProtectedInstallation.ValidateTree(LoginConfiguration.InstallDirectory);
    }

    public static void ValidateDirectory(string path, bool privateData = false) => Validate(new DirectoryInfo(path), privateData);
    public static void ValidateFile(string path, bool privateData = false) => Validate(new FileInfo(path), privateData);

    private static void Validate(FileSystemInfo info, bool privateData)
    {
        ProtectedInstallation.ValidateItem(info, privateData);
    }

    public static void CreateProtectedDirectory(string path)
    {
        var acl = new DirectorySecurity();
        acl.SetSecurityDescriptorSddlForm("O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", AccessControlSections.Access | AccessControlSections.Owner);
        new DirectoryInfo(path).Create(acl);
        // If another process created it first, do not change its ACL/owner.
        // Validate it and refuse redirections or untrusted pre-created paths.
        ValidateDirectory(path, privateData: true);
    }

    public static FileStream CreateProtectedFile(string path)
    {
        var acl = new FileSecurity();
        acl.SetSecurityDescriptorSddlForm("O:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)", AccessControlSections.Access | AccessControlSections.Owner);
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, acl);
    }
}
