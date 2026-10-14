using PatchLight.Windows;

namespace PatchLight.Tests;

public sealed class InstallLocationTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-install-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Folder_a_normal_user_can_write_is_not_safe()
    {
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "PatchLight.exe");
        File.WriteAllText(exe, "");
        Assert.False(InstallLocation.IsAdminOnly(exe));
    }

    [Fact]
    public void Windows_own_folders_are_safe() =>
        Assert.True(InstallLocation.IsAdminOnly(Path.Combine(Environment.SystemDirectory, "notepad.exe")));

    [Fact]
    public void Program_files_is_safe() =>
        Assert.True(InstallLocation.IsAdminOnly(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")));

    [Fact]
    public void Missing_file_is_not_safe() =>
        Assert.False(InstallLocation.IsAdminOnly(Path.Combine(dir, "nope.exe")));
}
