using System.Security.AccessControl;
using System.Security.Principal;

namespace CSIViewer.Platform;

internal enum InstallationIssue { Missing, OutsideProgramFiles, RedirectedPath, UntrustedOwner, UnsafePermissions, Inaccessible }

internal sealed class InstallationValidationException(InstallationIssue issue, string path)
    : UnauthorizedAccessException($"Installation validation failed: {issue}. Path: {path}")
{
    public InstallationIssue Issue { get; } = issue;
    public string ItemPath { get; } = path;
}

/// <summary>Shared policy for privileged binaries, not for ordinary per-user installs.</summary>
internal static class ProtectedInstallation
{
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";
    // Windows Modules Installer is an OS trust principal, not a standard user.
    private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private static bool IsTrusted(string? sid, bool privateData) =>
        sid is SystemSid or AdministratorsSid || (!privateData && sid == TrustedInstallerSid);

    public static void ValidateItem(FileSystemInfo item, bool privateData = false)
    {
        try
        {
            item.Refresh();
            if (!item.Exists) throw new InstallationValidationException(InstallationIssue.Missing, item.FullName);
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InstallationValidationException(InstallationIssue.RedirectedPath, item.FullName);
            FileSystemSecurity acl = item is DirectoryInfo directory
                ? directory.GetAccessControl() : ((FileInfo)item).GetAccessControl();
            if (!IsTrusted(acl.GetOwner(typeof(SecurityIdentifier))?.Value, privateData))
                throw new InstallationValidationException(InstallationIssue.UntrustedOwner, item.FullName);
            const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.AppendData |
                FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete |
                FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                // An inheritance-only ACE does not grant access to this object.
                // Every child is checked separately with its effective inherited ACEs.
                if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                if (rule.AccessControlType == AccessControlType.Allow && !IsTrusted(rule.IdentityReference.Value, privateData) &&
                    (privateData || (rule.FileSystemRights & write) != 0))
                    throw new InstallationValidationException(InstallationIssue.UnsafePermissions, item.FullName);
            }
        }
        catch (InstallationValidationException) { throw; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            throw new InstallationValidationException(InstallationIssue.Inaccessible, item.FullName);
        }
    }

    public static void ValidateProgramFilesParents(string path)
    {
        var anchor = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(anchor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InstallationValidationException(InstallationIssue.OutsideProgramFiles, fullPath);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(fullPath)!); directory is not null; directory = directory.Parent)
        {
            ValidateItem(directory);
            if (string.Equals(directory.FullName, anchor, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    public static void ValidateTree(string path)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(path));
        while (pending.TryPop(out var directory))
        {
            ValidateItem(directory);
            // Check each entry before descending: never traverse junctions/symlinks.
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                ValidateItem(item);
                if (item is DirectoryInfo child) pending.Push(child);
            }
        }
    }
}
