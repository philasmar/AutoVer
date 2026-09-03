using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// Mirrors VersionCommandTests, but for Dockerfile-based projects — confirms the native
/// Dockerfile version handler (LABEL org.opencontainers.image.version) behaves exactly
/// like the .csproj/.nuspec path through the full `autover version` CLI flow.
/// </summary>
[Retry(3)]
public class DockerfileVersionCommandTests
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

    // A. First-ever `version` run with no autover.json yet: auto-discovers the Dockerfile,
    // bumps using the default increment type (Patch), commits, tags — same as the .csproj case.
    [Test]
    public async Task FirstRun_NoConfig_AutoDiscoversAndBumpsPatch()
    {
        var dockerfilePath = await IOUtilities.CreateDockerfile(Path.Combine(_tempDir, "src", "Project1"));
        await IOUtilities.SetDockerfileVersion(dockerfilePath, "1.0.0");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(IOUtilities.AutoVerConfigExists(_tempDir)).IsFalse();
    }

    // B/C. Single change file drives a single-project version bump, for all three increment types.
    [Test]
    [Arguments(IncrementType.Patch, "1.0.1")]
    [Arguments(IncrementType.Minor, "1.1.0")]
    [Arguments(IncrementType.Major, "2.0.0")]
    public async Task ChangeFile_SingleProject_BumpsToExpectedVersion(IncrementType incrementType, string expectedVersion)
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "1.0.0");

        await IOUtilities.AddChangeFile("MyImage", incrementType, "A change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo(expectedVersion);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        // Same as .csproj: `version` only reads change files, it never deletes them.
        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(1);
    }

    // D. Two change files for the same project with different increment types: dominant wins.
    [Test]
    public async Task ChangeFile_MultipleFilesSameProject_DominantIncrementTypeWins()
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "3.0.0");

        await IOUtilities.AddChangeFile("MyImage", IncrementType.Patch, "Small change", _tempDir);
        await IOUtilities.AddChangeFile("MyImage", IncrementType.Major, "Big change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Two changes");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("4.0.0");
    }

    // E. Two independent Dockerfile-based projects bump independently.
    [Test]
    public async Task TwoProjects_IndependentChanges_BumpIndependently()
    {
        var (apiPath, workerPath) = await SetUpTwoDockerfileRepo("ApiImage", "1.0.0", "WorkerImage", "2.0.0", useSameVersionForAllProjects: false);

        await IOUtilities.AddChangeFile("ApiImage", IncrementType.Patch, "Api change", _tempDir);
        await IOUtilities.AddChangeFile("WorkerImage", IncrementType.Minor, "Worker change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Two independent changes");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(apiPath)).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetDockerfileVersion(workerPath)).IsEqualTo("2.1.0");
    }

    // F. UseSameVersionForAllProjects: true — both converge to the same next version even
    // though only one project has a recorded change.
    [Test]
    public async Task TwoProjects_UseSameVersionForAllProjects_BothConvergeToMaxVersion()
    {
        var (apiPath, workerPath) = await SetUpTwoDockerfileRepo("ApiImage", "1.0.0", "WorkerImage", "1.0.0", useSameVersionForAllProjects: true);

        await IOUtilities.AddChangeFile("ApiImage", IncrementType.Minor, "Only Api changed", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "One change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(apiPath)).IsEqualTo("1.1.0");
        await Assert.That(await IOUtilities.GetDockerfileVersion(workerPath)).IsEqualTo("1.1.0");
    }

    // G. `--use-version` forces the exact version, ignoring increment-type math entirely.
    [Test]
    public async Task UseVersionOption_ForcesExactVersion()
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "1.0.0", changeFilesDetermineIncrementType: false);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir, "--use-version", "9.9.9"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("9.9.9");
    }

    // H. `--no-commit` updates and stages the Dockerfile on disk, but leaves the repo dirty
    // with no new commit and no tag (tag creation is nested inside the "not no-commit" branch).
    [Test]
    public async Task NoCommitOption_LeavesChangesStagedButUncommitted()
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "1.0.0", changeFilesDetermineIncrementType: false);
        var commitCountBefore = GitUtilities.GetCommitCount(_tempDir);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir, "--no-commit"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitCountBefore);
        await Assert.That(GitUtilities.HasStagedChanges(_tempDir)).IsTrue();
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // I. `--no-tag` still commits the version bump, but skips creating a git tag.
    [Test]
    public async Task NoTagOption_CommitsButSkipsTag()
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "1.0.0", changeFilesDetermineIncrementType: false);
        var commitCountBefore = GitUtilities.GetCommitCount(_tempDir);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir, "--no-tag"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitCountBefore + 1);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // J. A Dockerfile with no version LABEL is seeded from the configured initial version rather
    // than rejected - the label is created, so the release after this one increments normally.
    [Test]
    public async Task MissingVersionLabel_IsSeededFromTheInitialVersion()
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "1.0.0", changeFilesDetermineIncrementType: false);
        await IOUtilities.RemoveDockerfileVersionLabel(dockerfilePath);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Remove version label");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("at AutoVer.");
        // The default initial version, taken as-is rather than incremented.
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath))
            .IsEqualTo(UserConfiguration.DefaultInitialVersion);

        // The label now exists, so the next release increments from it in the ordinary way.
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("0.1.1");
    }

    // K. Running `version` twice on the same calendar day produces `release_<date>` then
    // `release_<date>_2`.
    [Test]
    public async Task TwoRunsSameDay_TagsGetIncrementingSuffix()
    {
        var dockerfilePath = await SetUpSingleDockerfileRepo("MyImage", "1.0.0");

        await IOUtilities.AddChangeFile("MyImage", IncrementType.Patch, "First change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "First change");

        var firstRunExitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);
        await Assert.That(firstRunExitCode).IsEqualTo(CommandReturnCodes.Success);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEquivalentTo([$"release_{today}"]);

        await IOUtilities.AddChangeFile("MyImage", IncrementType.Patch, "Second change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Second change");

        var secondRunExitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);
        await Assert.That(secondRunExitCode).IsEqualTo(CommandReturnCodes.Success);

        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("1.0.2");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEquivalentTo([$"release_{today}", $"release_{today}_2"]);
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo($"Release {today} #2");
    }

    // Confirms the pluggable project-file-handler dispatch works correctly when a .csproj
    // and a Dockerfile are versioned together in the same repo/run.
    [Test]
    public async Task MixedProjectTypes_CsprojAndDockerfile_BothBumpIndependently()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");

        var dockerfilePath = await IOUtilities.CreateDockerfile(Path.Combine(_tempDir, "src", "Project1"));
        await IOUtilities.SetDockerfileVersion(dockerfilePath, "1.0.0");

        var autoVerFile =
@"{
    ""Projects"": [
        {
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        },
        {
            ""Name"": ""Project1Image"",
            ""Path"": ""src/Project1/Dockerfile""
        }
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}";
        await IOUtilities.AddAutoVerFile(_tempDir, autoVerFile);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Project1 change", _tempDir);
        await IOUtilities.AddChangeFile("Project1Image", IncrementType.Minor, "Image change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Two changes");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("1.1.0");
    }

    private async Task<string> SetUpSingleDockerfileRepo(string imageName, string initialVersion, bool changeFilesDetermineIncrementType = true)
    {
        var dockerfilePath = await IOUtilities.CreateDockerfile(_tempDir);
        await IOUtilities.SetDockerfileVersion(dockerfilePath, initialVersion);

        var autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""{imageName}"",
            ""Path"": ""Dockerfile""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": {changeFilesDetermineIncrementType.ToString().ToLower()}
}}";
        await IOUtilities.AddAutoVerFile(_tempDir, autoVerFile);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        return dockerfilePath;
    }

    private async Task<(string ApiPath, string WorkerPath)> SetUpTwoDockerfileRepo(
        string apiImageName, string apiVersion, string workerImageName, string workerVersion, bool useSameVersionForAllProjects)
    {
        var apiPath = await IOUtilities.CreateDockerfile(Path.Combine(_tempDir, "services", "api"));
        await IOUtilities.SetDockerfileVersion(apiPath, apiVersion);

        var workerPath = await IOUtilities.CreateDockerfile(Path.Combine(_tempDir, "services", "worker"));
        await IOUtilities.SetDockerfileVersion(workerPath, workerVersion);

        var autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""{apiImageName}"",
            ""Path"": ""services/api/Dockerfile""
        }},
        {{
            ""Name"": ""{workerImageName}"",
            ""Path"": ""services/worker/Dockerfile""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": {useSameVersionForAllProjects.ToString().ToLower()},
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";
        await IOUtilities.AddAutoVerFile(_tempDir, autoVerFile);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        return (apiPath, workerPath);
    }
}
