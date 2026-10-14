using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using PatchLight.Core;

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
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new PatchLightException($"{path} is a link to another folder, so PatchLight won't use it. Delete it and try again.");

        // Anything a normal user put here (links pointing elsewhere, or files they own and could
        // rewrite later) is removed rather than adopted. It is only cached pages and logs.
        // This happens before the new permissions are applied, so they never reach a link's target.
        RemoveUntrusted(dir);

        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(security);
        TryTakeOwnership(dir);
    }

    private static void RemoveUntrusted(DirectoryInfo dir)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos())
        {
            var isLink = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (!isLink && IsTrustedOwner(entry) && (entry is DirectoryInfo || LinkCount(entry.FullName) == 1))
            {
                if (entry is DirectoryInfo sub) RemoveUntrusted(sub);
                continue;
            }

            // Deleting a link removes only the link, never what it points to.
            if (entry is DirectoryInfo subDir) Directory.Delete(subDir.FullName, recursive: !isLink);
            else File.Delete(entry.FullName);
        }
    }

    private static bool IsTrustedOwner(FileSystemInfo entry)
    {
        var owner = entry is DirectoryInfo d
            ? d.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))
            : ((FileInfo)entry).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        return owner == System || owner == Admins;
    }

    // A file with more than one name is a hard link, possibly to something outside this folder.
    private static uint LinkCount(string file)
    {
        using var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return GetFileInformationByHandle(handle, out var info) ? info.NumberOfLinks : uint.MaxValue;
    }

    // Only works elevated. Without it the folder is still locked for everyone but its owner.
    private static void TryTakeOwnership(DirectoryInfo dir)
    {
        try
        {
            var security = new DirectorySecurity();
            security.SetOwner(Admins);
            dir.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or PrivilegeNotHeldException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] // FILETIME fields are only 4-byte aligned
    private struct FileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
}
