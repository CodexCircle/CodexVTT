using CodexVtt.Core;

namespace CodexVtt.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Default_data_root_is_outside_the_folder_managed_by_the_installer()
    {
        // Un percorso relativo che sale di un livello ("..") è un fratello della cartella dell'installer, non un figlio.
        var relative = Path.GetRelativePath(AppPaths.InstallerRoot, AppPaths.DefaultRoot);

        Assert.StartsWith("..", relative);
    }

    [Fact]
    public void Temp_files_of_interrupted_jobs_are_removed_at_startup()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(Path.Combine(dir.Path, "dati"));
        paths.EnsureCreated();
        var leftover = Path.Combine(paths.Temp, "job.pcm");
        File.WriteAllText(leftover, "x");

        paths.CleanTemp();

        Assert.False(File.Exists(leftover));
    }
}
