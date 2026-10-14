using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PatchLight.Windows;

// The daily check runs as SYSTEM and acts on what it reads here, so only
// administrators may change these files. Everyone else can read them.
[SupportedOSPlatform("windows")]
public static class DataFolder
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public static void Secure(string path)
    {
        var dir = Directory.CreateDirectory(path);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(security);
        TryTakeOwnership(dir);

        // Files made earlier by a normal user keep that user as owner, and owners can rewrite permissions.
        foreach (var entry in dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            // Drop any permissions of its own and take the folder's.
            FileSystemSecurity reset = entry is DirectoryInfo ? new DirectorySecurity() : new FileSecurity();
            reset.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            Apply(entry, reset);
            TryTakeOwnership(entry);
        }
    }

    // Only works elevated. Without it the folder is still locked for everyone but its owner.
    private static void TryTakeOwnership(FileSystemInfo entry)
    {
        try
        {
            FileSystemSecurity security = entry is DirectoryInfo ? new DirectorySecurity() : new FileSecurity();
            security.SetOwner(Admins);
            Apply(entry, security);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or PrivilegeNotHeldException)
        {
        }
    }

    private static void Apply(FileSystemInfo entry, FileSystemSecurity security)
    {
        if (entry is DirectoryInfo dir) dir.SetAccessControl((DirectorySecurity)security);
        else ((FileInfo)entry).SetAccessControl((FileSecurity)security);
    }
}
