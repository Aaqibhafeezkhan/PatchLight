using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using PatchLight.Windows;

namespace PatchLight.Tests;

public sealed class DataFolderTests : IDisposable
{
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-acl-" + Guid.NewGuid());

    public void Dispose()
    {
        if (!Directory.Exists(dir)) return;
        // As the owner we can always give ourselves access back, even when not elevated.
        var me = WindowsIdentity.GetCurrent().User!;
        foreach (var path in Directory.GetFileSystemEntries(dir).Append(dir))
        {
            var security = new FileInfo(path).GetAccessControl();
            security.SetAccessRuleProtection(false, false);
            security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        Directory.Delete(dir, recursive: true);
    }

    private static List<FileSystemAccessRule> Rules(FileSystemSecurity security) =>
        security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();

    [Fact]
    public void Only_system_and_administrators_can_change_the_folder()
    {
        DataFolder.Secure(dir);

        var security = new DirectoryInfo(dir).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var rules = Rules(security);
        Assert.Contains(rules, r => r.IdentityReference == System && r.FileSystemRights == FileSystemRights.FullControl);
        Assert.Contains(rules, r => r.IdentityReference == Admins && r.FileSystemRights == FileSystemRights.FullControl);
        Assert.Contains(rules, r => r.IdentityReference == Users && r.AccessControlType == AccessControlType.Allow);
        Assert.All(rules.Where(r => r.IdentityReference != System && r.IdentityReference != Admins),
            r => Assert.Equal(0, (int)(r.FileSystemRights & WriteRights)));
    }

    [Fact]
    public void Files_left_by_a_normal_user_are_removed_not_adopted()
    {
        // Running these tests unelevated, everything we create is owned by us, a normal user.
        Directory.CreateDirectory(Path.Combine(dir, "cache"));
        File.WriteAllText(Path.Combine(dir, "cache", "page.html"), "x");
        File.WriteAllText(Path.Combine(dir, "holds.json"), "[]");

        DataFolder.Secure(dir);

        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public void Hard_link_to_an_outside_file_is_removed_and_the_file_left_alone()
    {
        var outside = Path.Combine(Path.GetTempPath(), "patchlight-outside-" + Guid.NewGuid() + ".txt");
        File.WriteAllText(outside, "secret");
        try
        {
            Directory.CreateDirectory(dir);
            var link = Path.Combine(dir, "holds.json");
            Assert.True(CreateHardLink(link, outside, IntPtr.Zero));
            var before = new FileInfo(outside).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            DataFolder.Secure(dir);

            Assert.False(File.Exists(link));
            Assert.Equal("secret", File.ReadAllText(outside));
            Assert.Equal(before, new FileInfo(outside).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Junction_inside_is_removed_and_its_target_left_alone()
    {
        var target = Path.Combine(Path.GetTempPath(), "patchlight-target-" + Guid.NewGuid());
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        try
        {
            Directory.CreateDirectory(dir);
            var junction = Path.Combine(dir, "cache");
            MakeJunction(junction, target);
            var before = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            DataFolder.Secure(dir);

            Assert.False(Directory.Exists(junction));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
            Assert.Equal(before, new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void Refuses_a_data_folder_that_is_itself_a_link()
    {
        var target = Path.Combine(Path.GetTempPath(), "patchlight-target-" + Guid.NewGuid());
        Directory.CreateDirectory(target);
        try
        {
            MakeJunction(dir, target);
            Assert.Throws<PatchLight.Core.PatchLightException>(() => DataFolder.Secure(dir));
            Directory.Delete(dir);
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private static void MakeJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);
}
