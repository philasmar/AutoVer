using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// A repository whose version lives in its release tags rather than in a project file - the shape of
/// a shared CI templates repository, which has no artifact to carry a version and is consumed by
/// pinned ref. There is deliberately no .csproj, .nuspec or Dockerfile anywhere in these repos.
/// </summary>
[Retry(3)]
public class VersionFromTagCommandTests
{
    private const string SemverTagFormat = "v{major}.{minor}.{patch}";

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

    // The first release has no earlier tag to increment from, so it takes the configured initial
    // version exactly as given rather than incrementing it.
    [Test]
    [Arguments("1.0.0", "v1.0.0")]
    [Arguments(null, "v0.1.0")]
    public async Task FirstRelease_TakesTheInitialVersionAsIs(string? initialVersion, string expectedTag)
    {
        await SetUpRepository(initialVersion);
        await AddChangeFile(IncrementType.Minor, "The first feature");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("at AutoVer.");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains(expectedTag);
    }

    // Nothing carries the version, so a release writes no file and produces no version-bump commit.
    [Test]
    public async Task Release_WritesNoFileAndCreatesNoCommit()
    {
        await SetUpRepository("1.0.0");
        await AddChangeFile(IncrementType.Minor, "The first feature");

        var commitsBefore = GitUtilities.GetCommitCount(_tempDir);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitsBefore);
        await Assert.That(GitUtilities.HasUncommittedChanges(_tempDir)).IsFalse();
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.0");
    }

    // The second release reads the version back out of the first release's tag.
    [Test]
    public async Task SubsequentRelease_IncrementsTheVersionReadBackFromTheTag()
    {
        await SetUpRepository("1.0.0");

        await AddChangeFile(IncrementType.Minor, "The first feature");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await AddChangeFile(IncrementType.Patch, "A later fix");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.0.0");
        await Assert.That(tags).Contains("v1.0.1");
    }

    // One tag carries one version for the whole repository, so the largest increment asked for wins.
    [Test]
    public async Task IncrementType_IsTheLargestAnyProjectAskedFor()
    {
        await SetUpRepository("1.0.0", "docs", "templates");

        // A change file for one of the named projects is enough to make a release.
        await AddChangeFile(IncrementType.Minor, "The first feature", "docs");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.0");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        // A Patch on one project and a Major on the other: the release is a Major.
        await AddChangeFile(IncrementType.Patch, "A small docs fix", "docs");
        await AddChangeFile(IncrementType.Major, "A breaking template change", "templates");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v2.0.0");
    }

    [Test]
    public async Task Changelog_LabelsTheReleaseWithTheVersionFromTheTag()
    {
        await SetUpRepository("1.0.0");
        await AddChangeFile(IncrementType.Minor, "The first feature");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains("Release 1.0.0");
        await Assert.That(changelog).Contains("### ci (1.0.0)");
        await Assert.That(changelog).Contains("The first feature");

        var (tagExitCode, tagName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);
        await Assert.That(tagExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagName.Trim()).IsEqualTo("v1.0.0");
    }

    [Test]
    public async Task Current_PrintsTheVersionFromTheTagAndFailsBeforeAnyRelease()
    {
        await SetUpRepository("1.0.0");

        var (beforeExitCode, _, beforeError) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir, "--current"]);
        await Assert.That(beforeExitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(beforeError).Contains("autover version");
        await Assert.That(beforeError).DoesNotContain("at AutoVer.");

        await AddChangeFile(IncrementType.Minor, "The first feature");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var (afterExitCode, output, _) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir, "--current"]);
        await Assert.That(afterExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output.Trim()).IsEqualTo("1.0.0");
    }

    // "Nothing was asked for" has to mean no release whether or not the repository has released
    // before. Deciding it after the first-release branch let a first release be cut from an empty set
    // of changes while every release after it was correctly declined.
    [Test]
    public async Task NoChangeFiles_ReleasesNothing_OnTheFirstReleaseToo()
    {
        await SetUpRepository("1.0.0");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();

        // And with something to release, it releases.
        await AddChangeFile(IncrementType.Minor, "The first feature");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.0");
    }

    // The tag is the only place a tag-sourced version lives, so a label the format can't render is
    // lost outright rather than merely missing from the tag.
    [Test]
    public async Task PrereleaseLabelWithNoPlaceholderToCarryIt_FailsCleanlyWithUserError() =>
        await AssertConfigurationRejected(
            @"""Projects"": [ { ""Name"": ""ci"", ""PrereleaseLabel"": ""beta"" } ],
    ""VersionFromTag"": true,
    ""TagFormat"": ""v{major}.{minor}.{patch}""",
            "PrereleaseLabel");

    // The label applies to the first release as well - otherwise configuring one would be ignored on
    // the first release and honoured on every release after it.
    [Test]
    public async Task PrereleaseLabel_AppliesToTheFirstReleaseAndAfter()
    {
        await IOUtilities.AddAutoVerFile(_tempDir,
            @"{
    ""Projects"": [ { ""Name"": ""ci"", ""PrereleaseLabel"": ""beta"" } ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true,
    ""VersionFromTag"": true,
    ""InitialVersion"": ""1.0.0"",
    ""TagFormat"": ""v{major}.{minor}.{patch}[-{prerelease}]""
}");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "README.md"), "# templates");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        await AddChangeFile(IncrementType.Minor, "The first feature");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await AddChangeFile(IncrementType.Patch, "A later fix");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.0.0-beta");
        await Assert.That(tags).Contains("v1.0.1-beta");
    }

    [Test]
    public async Task UseVersion_OverridesTheTagDerivedVersion()
    {
        await SetUpRepository("1.0.0");

        var exitCode = await AutoVerUtilities.InitializeApp()
            .Run(["version", "--project-path", _tempDir, "--use-version", "3.2.1"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v3.2.1");
    }

    // The release-branch workflow a shared CI templates repository uses. Nothing carries a version,
    // so `version` writes no file and creates no commit - which means the tag lands on the branch
    // point and the changelog commit that follows is the release's only content. The pipeline moves
    // the tag onto that commit afterwards; without it, merging the tag would deliver no changelog.
    [Test]
    public async Task ReleaseBranchFlow_TagCoversTheChangelogAndReachesTheTargetBranch()
    {
        await SetUpRepository("1.0.0");
        var defaultBranch = GitUtilities.GetCurrentBranch(_tempDir);
        await AddChangeFile(IncrementType.Minor, "The first templates");

        var branchPoint = GitUtilities.GetCommitCount(_tempDir);
        GitUtilities.CreateAndCheckoutBranch(_tempDir, "releases/next-release");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        // No file to write, so the release itself adds no commit.
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(branchPoint);

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var (tagExitCode, tagName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);
        await Assert.That(tagExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagName.Trim()).IsEqualTo("v1.0.0");

        GitUtilities.MoveTagToHead(_tempDir, tagName.Trim());

        // Merging the tag is what publishes the release, so the changelog has to travel with it.
        GitUtilities.CheckoutBranch(_tempDir, defaultBranch);
        GitUtilities.MergeNoFastForward(_tempDir, "releases/next-release");

        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains("Release 1.0.0");
        await Assert.That(changelog).Contains("The first templates");

        // And the release is still correctly identified from the merge commit.
        var (mergedExitCode, mergedTag, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);
        await Assert.That(mergedExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(mergedTag.Trim()).IsEqualTo("v1.0.0");
    }

    // The tag is the only place the version lives, so a date-based tag has nothing to read back.
    [Test]
    public async Task DateBasedTagFormat_FailsCleanlyWithUserError() =>
        await AssertConfigurationRejected(
            @"""Projects"": [ { ""Name"": ""ci"" } ],
    ""VersionFromTag"": true,
    ""TagFormat"": ""release_{date}""",
            "TagFormat");

    // One tag carries one version, so a file-backed project alongside a tag-sourced one is ambiguous.
    [Test]
    public async Task ProjectWithAPath_FailsCleanlyWithUserError()
    {
        await IOUtilities.CreateDockerfile(_tempDir);
        await IOUtilities.SetDockerfileVersion(Path.Combine(_tempDir, "Dockerfile"), "1.0.0");

        await AssertConfigurationRejected(
            @"""Projects"": [ { ""Name"": ""ci"", ""Path"": ""Dockerfile"" } ],
    ""VersionFromTag"": true,
    ""TagFormat"": ""v{major}.{minor}.{patch}""",
            "Path");
    }

    // A change file needs a project name to attach to, and the changelog needs one to label. Without
    // one, the message has to name that - not complain about missing .csproj/.nuspec/Dockerfile files.
    [Test]
    public async Task NoProjectsListed_FailsCleanlyNamingTheRealProblem()
    {
        await AssertConfigurationRejected(
            @"""Projects"": [],
    ""VersionFromTag"": true,
    ""TagFormat"": ""v{major}.{minor}.{patch}""",
            "no projects");

        var (_, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);
        await Assert.That(error).DoesNotContain(".csproj");
    }

    [Test]
    public async Task UnparseableInitialVersion_FailsCleanlyWithUserError() =>
        await AssertConfigurationRejected(
            @"""Projects"": [ { ""Name"": ""ci"" } ],
    ""VersionFromTag"": true,
    ""InitialVersion"": ""one"",
    ""TagFormat"": ""v{major}.{minor}.{patch}""",
            "three part version");

    private async Task AssertConfigurationRejected(string settings, string expectedInError)
    {
        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    {settings},
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true
}}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains(expectedInError);
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    private async Task SetUpRepository(string? initialVersion, params string[] projectNames)
    {
        var names = projectNames.Length > 0 ? projectNames : ["ci"];
        var projects = string.Join(", ", names.Select(name => $@"{{ ""Name"": ""{name}"" }}"));
        var initial = string.IsNullOrEmpty(initialVersion)
            ? ""
            : $@"
    ""InitialVersion"": ""{initialVersion}"",";

        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [ {projects} ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true,
    ""VersionFromTag"": true,{initial}
    ""TagFormat"": ""{SemverTagFormat}""
}}");

        // A README stands in for whatever the repository actually holds - deliberately nothing that
        // any project file handler would recognise.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "README.md"), "# templates");

        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");
    }

    private async Task AddChangeFile(IncrementType incrementType, string message, string projectName = "ci")
    {
        var changeFilePath = await IOUtilities.AddChangeFile(projectName, incrementType, message, _tempDir);
        GitUtilities.StageChanges(_tempDir, changeFilePath);
        GitUtilities.CommitChanges(_tempDir, message);
    }
}
