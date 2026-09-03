using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// Automates the manual "does .NET/.csproj versioning still work end to end" QA pass:
/// scenarios A-K, run against real git repos and real `dotnet new classlib` projects.
/// </summary>
[Retry(3)]
public class VersionCommandTests
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

    // A. First-ever `version` run with no autover.json yet: auto-discovers the project,
    // bumps using the default increment type (Patch), commits, tags — and does NOT
    // persist a config file, since one never existed to begin with.
    [Test]
    public async Task FirstRun_NoConfig_AutoDiscoversAndBumpsPatch()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(IOUtilities.AutoVerConfigExists(_tempDir)).IsFalse();
    }

    // `--project-path` must work with relative paths too, not just the absolute paths every
    // other test in this suite happens to use — this regressed to an unhandled-exception
    // crash ("Basepath argument is not fully qualified") because SetCurrentDirectory stored
    // the raw relative value instead of resolving it against the real process working
    // directory first. Runs the CLI as a real subprocess (rather than mutating
    // Environment.CurrentDirectory in-process, which leaks across concurrently-running test
    // bodies/hooks in this same test process) so each case gets its own genuine working directory.
    [Test]
    [Arguments(".")]
    [Arguments("src/Project1")]
    public async Task RelativeProjectPath_DoesNotCrash_AndBumpsVersion(string relativeProjectPath)
    {
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);

        var workingDirectory = relativeProjectPath == "."
            ? Path.Combine(_tempDir, "src", "Project1")
            : _tempDir;

        var (exitCode, _, error) = await IOUtilities.RunAutoVerCli(workingDirectory, "version", "--project-path", relativeProjectPath);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("Basepath argument is not fully qualified");
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.1");
    }

    // --project-path is free to point at any subdirectory of the repo, but when an existing
    // autover.json already fully describes the projects (which can live anywhere in the tree),
    // pointing it at some unrelated, project-free subdirectory must not force a discovery scan
    // that isn't even needed — RetrieveUserConfiguration previously called GetAvailableProjects
    // unconditionally and discarded the result whenever a config already existed, so an empty
    // subdirectory made the whole command fail even though nothing needed to be discovered.
    [Test]
    public async Task ProjectPathPointsAtUnrelatedSubdirectory_ExistingConfigIsUsedWithoutDiscovery()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);
        var docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", docsDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.1");
    }

    // Change files live under .autover/changes at the git root, not under --project-path.
    // End-to-end confirmation that pointing --project-path at the project's own (non-root)
    // subdirectory still finds them. (This doesn't by itself isolate ChangeFileHandler's
    // internal absolute- vs relative-path handling — ConfigurationManager.RetrieveUserConfiguration
    // always re-points the shared current directory at the git root before change-file lookup
    // ever runs, so that ambient state is already correct here regardless; see
    // ChangeFileHandlerTest for a test that isolates the handler from that ambient state.)
    [Test]
    public async Task ProjectPathPointsAtProjectSubdirectory_StillFindsChangeFilesAtGitRoot()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0");
        var projectDir = Path.Combine(_tempDir, "src", "Project1");

        await IOUtilities.AddChangeFile("Project1", IncrementType.Minor, "A change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", projectDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.1.0");
    }

    // B/C. Single change file drives a single-project version bump; verified for all three
    // increment types to lock in the semver math (Minor resets patch, Major resets minor+patch).
    [Test]
    [Arguments(IncrementType.Patch, "1.0.1")]
    [Arguments(IncrementType.Minor, "1.1.0")]
    [Arguments(IncrementType.Major, "2.0.0")]
    public async Task ChangeFile_SingleProject_BumpsToExpectedVersion(IncrementType incrementType, string expectedVersion)
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0");

        await IOUtilities.AddChangeFile("Project1", incrementType, "A change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo(expectedVersion);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        // `version` only reads change files to determine the increment type — it never
        // deletes them. Only `changelog` consumes/deletes change files (see ChangelogCommandTests).
        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(1);
    }

    // D. Two change files for the same project with different increment types: the
    // dominant (highest) type wins, not whichever was written last.
    [Test]
    public async Task ChangeFile_MultipleFilesSameProject_DominantIncrementTypeWins()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "3.0.0");

        await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Small change", _tempDir);
        await IOUtilities.AddChangeFile("Project1", IncrementType.Major, "Big change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Two changes");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("4.0.0");
    }

    // E. Two independent projects, each with their own change file, bump independently
    // when UseSameVersionForAllProjects is false (the default).
    [Test]
    public async Task TwoProjects_IndependentChanges_BumpIndependently()
    {
        var (project1Path, project2Path) = await SetUpTwoProjectRepo("Project1", "1.0.0", "Project2", "2.0.0", useSameVersionForAllProjects: false);

        await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Project1 change", _tempDir);
        await IOUtilities.AddChangeFile("Project2", IncrementType.Minor, "Project2 change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Two independent changes");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(project1Path)).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(project2Path)).IsEqualTo("2.1.0");
    }

    // F. With UseSameVersionForAllProjects: true, only one project has a recorded change,
    // but BOTH converge to the same next version.
    [Test]
    public async Task TwoProjects_UseSameVersionForAllProjects_BothConvergeToMaxVersion()
    {
        var (project1Path, project2Path) = await SetUpTwoProjectRepo("Project1", "1.0.0", "Project2", "1.0.0", useSameVersionForAllProjects: true);

        await IOUtilities.AddChangeFile("Project1", IncrementType.Minor, "Only Project1 changed", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "One change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(project1Path)).IsEqualTo("1.1.0");
        await Assert.That(await IOUtilities.GetProjectVersion(project2Path)).IsEqualTo("1.1.0");
    }

    // G. `--use-version` forces the exact version, ignoring increment-type math entirely.
    [Test]
    public async Task UseVersionOption_ForcesExactVersion()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir, "--use-version", "9.9.9"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("9.9.9");
    }

    // H. `--no-commit` updates and stages the csproj on disk, but leaves the repo dirty
    // with no new commit. Since tag creation is nested inside the "not no-commit" branch
    // in VersionCommand, no tag is created either — that's intentional current behavior,
    // not an oversight in this test.
    [Test]
    public async Task NoCommitOption_LeavesChangesStagedButUncommitted()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);
        var commitCountBefore = GitUtilities.GetCommitCount(_tempDir);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir, "--no-commit"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitCountBefore);
        await Assert.That(GitUtilities.HasStagedChanges(_tempDir)).IsTrue(); // staged by `version`, just never committed
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // I. `--no-tag` still commits the version bump, but skips creating a git tag.
    [Test]
    public async Task NoTagOption_CommitsButSkipsTag()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);
        var commitCountBefore = GitUtilities.GetCommitCount(_tempDir);

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir, "--no-tag"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitCountBefore + 1);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // J. A project with no <Version> tag is seeded from the configured initial version rather than
    // rejected - the element is created, so the release after this one increments normally.
    [Test]
    public async Task MissingVersionTag_IsSeededFromTheInitialVersion()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);
        await IOUtilities.RemoveProjectVersionTag(csprojPath);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Remove version tag");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("at AutoVer.");
        // The default initial version, taken as-is rather than incremented.
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath))
            .IsEqualTo(UserConfiguration.DefaultInitialVersion);

        // The element now exists, so the next release increments from it in the ordinary way.
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("0.1.1");
    }

    // A version field that exists but is empty is the same situation as one that is missing - the
    // project carries no version - so it is seeded rather than incremented from nothing.
    [Test]
    public async Task EmptyVersionTag_IsSeededFromTheInitialVersion()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");
        var withEmptyVersion = (await File.ReadAllTextAsync(csprojPath))
            .Replace("<Version>1.0.0</Version>", "<Version></Version>");
        await File.WriteAllTextAsync(csprojPath, withEmptyVersion);

        await IOUtilities.AddAutoVerFile(_tempDir,
            @"{
    ""Projects"": [ { ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" } ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": false,
    ""InitialVersion"": ""5.0.0""
}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("5.0.0");
    }

    // A configured InitialVersion applies to a project file too, not only to a tag-sourced
    // repository - the seeded version is the one that was asked for.
    [Test]
    public async Task MissingVersionTag_UsesAConfiguredInitialVersion()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0", changeFilesDetermineIncrementType: false);
        await IOUtilities.RemoveProjectVersionTag(csprojPath);
        await IOUtilities.AddAutoVerFile(_tempDir,
            @"{
    ""Projects"": [ { ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" } ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": false,
    ""InitialVersion"": ""2.5.0""
}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Remove version tag");

        var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("2.5.0");
    }

    // K. Running `version` twice on the same calendar day produces `release_<date>` then
    // `release_<date>_2`, matching VersionHandler's per-day tag counter.
    [Test]
    public async Task TwoRunsSameDay_TagsGetIncrementingSuffix()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.0.0");

        await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "First change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "First change");

        var firstRunExitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);
        await Assert.That(firstRunExitCode).IsEqualTo(CommandReturnCodes.Success);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEquivalentTo([$"release_{today}"]);

        await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Second change", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Second change");

        var secondRunExitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);
        await Assert.That(secondRunExitCode).IsEqualTo(CommandReturnCodes.Success);

        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.2");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEquivalentTo([$"release_{today}", $"release_{today}_2"]);
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo($"Release {today} #2");
    }

    // L. `--current` prints the already-set version and exits, without incrementing,
    // committing, or tagging anything - callers (e.g. a pipeline pushing a container tag)
    // need to read a project's current version without accidentally bumping it.
    [Test]
    public async Task Current_PrintsVersionWithoutMutating()
    {
        var csprojPath = await SetUpSingleProjectRepo("Project1", "1.2.3", changeFilesDetermineIncrementType: false);
        var commitCountBefore = GitUtilities.GetCommitCount(_tempDir);

        var (exitCode, output, _) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir, "--current"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output.Trim()).IsEqualTo("1.2.3");
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.2.3");
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitCountBefore);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // `changeFilesDetermineIncrementType` must be true for scenarios that rely on a change
    // file to pick the increment type (B/C/D/K), and false for scenarios with no change
    // file at all (G/H/I/J) — with it true and no change file, VersionCommand resolves the
    // project's increment type to None and silently skips it before ever touching the
    // .csproj, which would make those flag-behavior tests observe nothing happening.
    private async Task<string> SetUpSingleProjectRepo(string projectName, string initialVersion, bool changeFilesDetermineIncrementType = true)
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
    ""ChangeFilesDetermineIncrementType"": {changeFilesDetermineIncrementType.ToString().ToLower()}
}}";
        await IOUtilities.AddAutoVerFile(_tempDir, autoVerFile);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        return csprojPath;
    }

    private async Task<(string Project1Path, string Project2Path)> SetUpTwoProjectRepo(
        string project1Name, string project1Version, string project2Name, string project2Version, bool useSameVersionForAllProjects)
    {
        await IOUtilities.CreateProject(_tempDir, "src", project1Name);
        var project1Path = Path.Combine(_tempDir, "src", project1Name, $"{project1Name}.csproj");
        await IOUtilities.SetProjectVersion(project1Path, project1Version);

        await IOUtilities.CreateProject(_tempDir, "src", project2Name);
        var project2Path = Path.Combine(_tempDir, "src", project2Name, $"{project2Name}.csproj");
        await IOUtilities.SetProjectVersion(project2Path, project2Version);

        var autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""{project1Name}"",
            ""Path"": ""src/{project1Name}/{project1Name}.csproj""
        }},
        {{
            ""Name"": ""{project2Name}"",
            ""Path"": ""src/{project2Name}/{project2Name}.csproj""
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

        return (project1Path, project2Path);
    }
}
