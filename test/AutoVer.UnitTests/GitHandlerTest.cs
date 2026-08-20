using AutoVer.Services;
using AutoVer.UnitTests.Utilities;
using LibGit2Sharp;

namespace AutoVer.UnitTests;

/// <summary>
/// Regression coverage for GetFileByTag/GetFolderByTag crashing with an unhandled
/// "Sequence contains no matching element" LINQ exception when the requested path didn't
/// exist yet at the given tag (e.g. autover.json or a change file was added in a commit after
/// the last release tag). Both must resolve that like "nothing found at this tag" instead.
/// </summary>
public class GitHandlerTest
{
    private string _tempDir = string.Empty;

    [Before(Test)]
    public void Before()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        Repository.Init(_tempDir);
    }

    [After(Test)]
    public void After()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private async Task TagInitialCommit(string tagName)
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "README.md"), "hello");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial commit");
        using var repo = new Repository(_tempDir);
        repo.ApplyTag(tagName);
    }

    [Test]
    public async Task GetFileByTag_PathAddedAfterTag_ReturnsNull()
    {
        await TagInitialCommit("release_2026-08-20");

        Directory.CreateDirectory(Path.Combine(_tempDir, ".autover"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".autover", "autover.json"), "{}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Add autover.json after the tag");

        var gitHandler = AutoVerUtilities.GetService<IGitHandler>();
        var content = gitHandler.GetFileByTag(_tempDir, "release_2026-08-20", Path.Combine(".autover", "autover.json"));

        await Assert.That(content).IsNull();
    }

    [Test]
    public async Task GetFileByTag_PathExistsAtTag_ReturnsContent()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".autover"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".autover", "autover.json"), "{\"Foo\":true}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Add autover.json");
        using (var repo = new Repository(_tempDir))
            repo.ApplyTag("release_2026-08-20");

        var gitHandler = AutoVerUtilities.GetService<IGitHandler>();
        var content = gitHandler.GetFileByTag(_tempDir, "release_2026-08-20", Path.Combine(".autover", "autover.json"));

        await Assert.That(content).IsEqualTo("{\"Foo\":true}");
    }

    [Test]
    public async Task GetFolderByTag_FolderAddedAfterTag_ReturnsEmptyList()
    {
        await TagInitialCommit("release_2026-08-20");

        var changesDir = Path.Combine(_tempDir, ".autover", "changes");
        Directory.CreateDirectory(changesDir);
        await File.WriteAllTextAsync(Path.Combine(changesDir, "change.json"), "{}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Add a change file after the tag");

        var gitHandler = AutoVerUtilities.GetService<IGitHandler>();
        var files = gitHandler.GetFolderByTag(_tempDir, "release_2026-08-20", Path.Combine(".autover", "changes"));

        await Assert.That(files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetFolderByTag_FolderExistsAtTag_ReturnsFiles()
    {
        var changesDir = Path.Combine(_tempDir, ".autover", "changes");
        Directory.CreateDirectory(changesDir);
        await File.WriteAllTextAsync(Path.Combine(changesDir, "change.json"), "{\"Foo\":true}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Add a change file");
        using (var repo = new Repository(_tempDir))
            repo.ApplyTag("release_2026-08-20");

        var gitHandler = AutoVerUtilities.GetService<IGitHandler>();
        var files = gitHandler.GetFolderByTag(_tempDir, "release_2026-08-20", Path.Combine(".autover", "changes"));

        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0].Content).IsEqualTo("{\"Foo\":true}");
    }
}
