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
    public void Files_that_already_exist_are_locked_down_too()
    {
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "holds.json");
        File.WriteAllText(file, "[]");

        DataFolder.Secure(dir);

        var rules = Rules(new FileInfo(file).GetAccessControl());
        Assert.Contains(rules, r => r.IdentityReference == Users);
        Assert.All(rules.Where(r => r.IdentityReference != System && r.IdentityReference != Admins),
            r => Assert.Equal(0, (int)(r.FileSystemRights & WriteRights)));
    }
}
