namespace Trofeo.Modules.Agents.Tests;

public sealed class ProjectNamesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "trofeo-projects-" + Guid.NewGuid().ToString("N"));

    public ProjectNamesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ProjectName_PlainRepo()
    {
        var repo = Dir("app");
        Dir("app", ".git");
        Assert.Equal("app", ProjectNames.FromWorkingDirectory(repo));
    }

    [Fact]
    public void ProjectName_Subfolder_UsesRepoRoot()
    {
        Dir("app", ".git");
        var sub = Dir("app", "src", "Core");
        Assert.Equal("app", ProjectNames.FromWorkingDirectory(sub));
    }

    [Fact]
    public void ProjectName_Worktree_UsesMainRepo()
    {
        Dir("app", ".git", "worktrees", "feat");
        var worktree = Dir("app", ".claude", "worktrees", "feat");
        var gitdir = Path.Combine(_root, "app", ".git", "worktrees", "feat");
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: " + gitdir + "\n");
        Assert.Equal("app", ProjectNames.FromWorkingDirectory(worktree));
    }

    [Fact]
    public void ProjectName_Worktree_AcceptsBackslashes()
    {
        var worktree = Dir("wt", "feat");
        File.WriteAllText(Path.Combine(worktree, ".git"), @"gitdir: C:\Dev\mon-app\.git\worktrees\feat");
        Assert.Equal("mon-app", ProjectNames.FromWorkingDirectory(worktree));
    }

    [Fact]
    public void ProjectName_GitFileWithoutGitdir_UsesFolder()
    {
        // Un fichier .git sans ligne « gitdir: » ne dit rien : on retombe sur le nom du dossier.
        var folder = Dir("Mes projets");
        File.WriteAllText(Path.Combine(folder, ".git"), "");
        Assert.Equal("Mes projets", ProjectNames.FromWorkingDirectory(folder));
    }

    [Fact]
    public void ProjectName_NoGit_UsesFolder()
    {
        var folder = Dir("Mes projets");
        Assert.Equal("Mes projets", ProjectNames.FromWorkingDirectory(folder));
    }

    [Fact]
    public void ProjectName_TrailingSeparator_IsIgnored()
    {
        var folder = Dir("Mes projets");
        Assert.Equal("Mes projets", ProjectNames.FromWorkingDirectory(folder + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("/")]
    public void ProjectName_DriveRoot_ReturnsCwd(string root) =>
        Assert.Equal(root, ProjectNames.FromWorkingDirectory(root));
}
