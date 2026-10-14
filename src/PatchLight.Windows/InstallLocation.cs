using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PatchLight.Windows;

// The daily check runs PatchLight.exe as SYSTEM. If a normal user (or malware running as one)
// could replace that file or drop a DLL next to it, they would get SYSTEM rights. So the daily
// check is only allowed from a folder that only administrators can change, like Program Files.
[SupportedOSPlatform("windows")]
public static class InstallLocation
{
    private static readonly SecurityIdentifier[] Trusted =
    [
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
    ];

    // Lets someone swap out an existing file or folder (rename/delete it, or rewrite its permissions).
    private const FileSystemRights ReplaceRights = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // Adds plant-a-DLL and overwrite-the-file on top of that.
    private const FileSystemRights WriteRights = ReplaceRights | FileSystemRights.WriteData | FileSystemRights.AppendData;

    public static bool IsAdminOnly(string exePath)
    {
        try
        {
            var exe = new FileInfo(Path.GetFullPath(exePath));
            if (!exe.Exists || !IsSafe(exe, WriteRights)) return false;

            // The program's own folder must not accept new files; folders above it must not let anyone swap it out.
            var rights = WriteRights;
            for (var dir = exe.Directory; dir is not null; dir = dir.Parent, rights = ReplaceRights)
            {
                if (!IsSafe(dir, rights)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafe(FileSystemInfo entry, FileSystemRights dangerous)
    {
        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;

        FileSystemSecurity security = entry is DirectoryInfo d ? d.GetAccessControl() : ((FileInfo)entry).GetAccessControl();
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted.Contains(owner))
            return false;

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            // Inherit-only entries apply to things inside, which are checked on their own.
            if (rule.AccessControlType != AccessControlType.Allow || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
                continue;
            if (!Trusted.Contains((SecurityIdentifier)rule.IdentityReference) && (rule.FileSystemRights & dangerous) != 0)
                return false;
        }
        return true;
    }
}
