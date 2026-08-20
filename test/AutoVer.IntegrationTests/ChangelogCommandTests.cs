using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// Automates scenario L from the manual QA pass, plus the notable finding that change
/// files are consumed by `changelog`, not by `version`.
/// </summary>
[Retry(3)]
public class ChangelogCommandTests
{
    private string _tempDir = string.Empty;

    [Before(Test)]
    public void Before()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        Repository.Init(_tempDir);
        using var repo = new Repository(_tempDir);
        _tempDir = repo.Info.WorkingDirectory;
        IOUtilities.AddGitignore(_tempDir);
    }

    [After(Test)]
    public void After()
    {
        try
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                IOUtilities.RemoveReadOnly(_tempDir);
                Directory.Delete(_tempDir, true);
            }
        }
        catch (Exception ex)
        {
            Assert.Fail(ex.Message);
        }
    }

    // Default `changelog`: writes CHANGELOG.md, commits "Updated changelog", and is the
    // command that actually deletes change files — `version` never touches them.
    [Test]
    public async Task DefaultChangelog_WritesFile_Commits_AndConsumesChangeFiles()
    {
        await SetUpAndVersionSingleProject("Project1", "1.0.0", "Important change");

        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(1);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["changelog", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(IOUtilities.ChangelogExists(_tempDir)).IsTrue();
        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("Important change");
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo("Updated changelog");
        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(0);
    }

    [Test]
    public async Task OutputToConsole_PrintsChangelog_DoesNotPersistOrConsumeChangeFiles()
    {
        await SetUpAndVersionSingleProject("Project1", "1.0.0", "Console change");
        var commitCountBefore = GitUtilities.GetCommitCount(_tempDir);

        var (exitCode, output, _) = await AutoVerUtilities.RunCapturingOutput(["changelog", "--project-path", _tempDir, "--output-to-console"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output).Contains("Console change");
        await Assert.That(IOUtilities.ChangelogExists(_tempDir)).IsFalse();
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitCountBefore);
        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(1);
    }

    [Test]
    public async Task ReleaseNameOption_PrintsReleaseTitleOnly()
    {
        await SetUpAndVersionSingleProject("Project1", "1.0.0", "A change");

        var (exitCode, output, _) = await AutoVerUtilities.RunCapturingOutput(["changelog", "--project-path", _tempDir, "--release-name"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output.Trim()).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
    }

    [Test]
    public async Task TagNameOption_PrintsTagNameOnly()
    {
        await SetUpAndVersionSingleProject("Project1", "1.0.0", "A change");

        var (exitCode, output, _) = await AutoVerUtilities.RunCapturingOutput(["changelog", "--project-path", _tempDir, "--tag-name"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output.Trim()).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");
    }

    private async Task SetUpAndVersionSingleProject(string projectName, string initialVersion, string changeMessage)
    {
        await IOUtilities.CreateProject(_tempDir, "src", projectName);
        var csprojPath = Path.Combine(_tempDir, "src", projectName, $"{projectName}.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, initialVersion);

        var autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""{projectName}"",
            ""Path"": ""src/{projectName}/{projectName}.csproj""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";
        await IOUtilities.AddAutoVerFile(_tempDir, autoVerFile);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        await IOUtilities.AddChangeFile(projectName, IncrementType.Patch, changeMessage, _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "A change");

        var versionExitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);
        if (versionExitCode != CommandReturnCodes.Success)
            throw new Exception($"Setup failed: `autover version` returned exit code {versionExitCode}.");
    }
}
