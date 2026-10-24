using CodexVtt.Core.History;

namespace CodexVtt.Core.Tests;

public class FolderProbeTests
{
    [Fact]
    public void Writable_folder_passes_and_leaves_no_test_file()
    {
        using var dir = new TempDir();

        var result = FolderProbe.Test(dir.Path);

        Assert.True(result.Ok);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void Missing_folder_is_created_for_the_test_and_removed_if_it_was_only_a_probe()
    {
        using var dir = new TempDir();
        var target = Path.Combine(dir.Path, "nuova");

        Assert.True(FolderProbe.Test(target).Ok);
        Assert.False(Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length > 0);
    }

    [Fact]
    public void Folder_under_a_file_fails()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", [1]);

        var result = FolderProbe.Test(Path.Combine(file, "sotto"));

        Assert.False(result.Ok);
        Assert.Equal(FolderTestResult.NotFound, result.Error);
    }

    [Fact]
    public void Relative_or_empty_path_fails()
    {
        Assert.False(FolderProbe.Test("").Ok);
        Assert.False(FolderProbe.Test("relativa").Ok);
    }

    [Fact]
    public void Read_only_folder_cannot_be_written()
    {
        if (!OperatingSystem.IsWindows()) return; // su Linux il test gira spesso come root
        using var dir = new TempDir();
        var locked = Path.Combine(dir.Path, "ro");
        Directory.CreateDirectory(locked);
        var info = new DirectoryInfo(locked);
        var acl = info.GetAccessControl();
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.CreateFiles | System.Security.AccessControl.FileSystemRights.WriteData,
            System.Security.AccessControl.AccessControlType.Deny);
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            var result = FolderProbe.Test(locked);
            Assert.False(result.Ok);
            Assert.Equal(FolderTestResult.CannotWrite, result.Error);
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
    }
}
