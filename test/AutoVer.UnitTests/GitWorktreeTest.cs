using System.Diagnostics;
using AutoVer.UnitTests.Utilities;
using LibGit2Sharp;

namespace AutoVer.UnitTests;

[Retry(3)]
public class GitWorktreeTest
{
    private string _tempDir = string.Empty;

    [Before(Test)]
    public void Before()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        Repository.Init(tempDir);
        using (var repo = new Repository(tempDir))
        {
            _tempDir = repo.Info.WorkingDirectory;
            IOUtilities.AddGitignore(repo.Info.WorkingDirectory);
        }
    }

    [Test]
    public async Task ChangeCommand_FromLinkedWorktree_WritesChangeFileInsideWorktree()
    {
        string mainRepoDir = _tempDir;

        // Set up a project in the main repo and commit it so we have something to branch from.
        await Assert.That(await IOUtilities.CreateProject(mainRepoDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(mainRepoDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";
        await IOUtilities.AddAutoVerFile(mainRepoDir, autoVerFile);
        GitUtilities.StageChanges(mainRepoDir, "*");
        GitUtilities.CommitChanges(mainRepoDir, "Initial Commit");

        // Add a linked worktree on a new branch. In a linked worktree `.git` is a file
        // (containing a `gitdir:` pointer) rather than a directory — this is the case
        // the fix in GitHandler.FindGitRootDirectory targets.
        var worktreeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await RunGit(mainRepoDir, "worktree", "add", "-b", "feature/wt", worktreeDir);
        await Assert.That(File.Exists(Path.Combine(worktreeDir, ".git"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(worktreeDir, ".git"))).IsFalse();

        // Run `autover change` from inside the worktree.
        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();
        var args = new[]
        {
            "change",
            "--project-path", worktreeDir,
            "--project-name", "Project1",
            "--increment-type", "Minor",
            "-m", "Worktree change"
        };
        var exitCode = await app!.Run(args);

        await Assert.That(exitCode).IsEqualTo(0);

        // The change file must be written inside the worktree, NOT in the main repo.
        var worktreeChangesDir = Path.Combine(worktreeDir, ".autover", "changes");
        var mainRepoChangesDir = Path.Combine(mainRepoDir, ".autover", "changes");

        await Assert.That(Directory.Exists(worktreeChangesDir)).IsTrue();
        var worktreeChangeFiles = Directory.GetFiles(worktreeChangesDir, "*.json");
        await Assert.That(worktreeChangeFiles.Length).IsEqualTo(1);

        var mainRepoChangeFiles = Directory.Exists(mainRepoChangesDir)
            ? Directory.GetFiles(mainRepoChangesDir, "*.json")
            : Array.Empty<string>();
        await Assert.That(mainRepoChangeFiles.Length).IsEqualTo(0);

        var content = await File.ReadAllTextAsync(worktreeChangeFiles[0]);
        await Assert.That(content).Contains("Worktree change");
        await Assert.That(content).Contains("Project1");
    }

    private static async Task RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception($"git {string.Join(' ', args)} failed (exit {process.ExitCode}): {stderr}{stdout}");
    }
}
