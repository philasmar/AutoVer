using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// End-to-end coverage for a configurable TagFormat/ReleaseNameFormat: a repository whose releases
/// are identified by version rather than by date, which is what lets a consumer pin an immutable
/// semver ref (e.g. a shared CI templates repo included at `ref: v1.4.0`).
/// </summary>
[Retry(3)]
public class TagFormatCommandTests
{
    private const string SemverTagFormat = "v{major}.{minor}.{patch}[-{iteration}]";
    private const string SemverReleaseNameFormat = "Release {major}.{minor}.{patch}[ #{iteration}]";

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

    [Test]
    public async Task SemverTagFormat_TagsAndNamesTheReleaseByVersion()
    {
        var csprojPath = await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);
        await AddChangeFile(IncrementType.Minor, "A new feature");

        var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.1.0");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.1.0");
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo("Release 1.1.0");
    }

    // Successive releases are distinguished by the version itself, so the iteration group stays
    // elided - the date-based default's "_2 for the second release today" has no analogue here.
    [Test]
    public async Task SemverTagFormat_SuccessiveReleases_AreDistinguishedByVersionNotIteration()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);

        await AddChangeFile(IncrementType.Patch, "First fix");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await AddChangeFile(IncrementType.Patch, "Second fix");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.0.1");
        await Assert.That(tags).Contains("v1.0.2");
    }

    // Re-releasing the same version has nowhere to go except the iteration group.
    [Test]
    public async Task SemverTagFormat_RepeatOfTheSameVersion_FallsBackToTheIteration()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);

        await Assert.That(await AutoVerUtilities.InitializeApp()
                .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.0"]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp()
                .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.0"]))
            .IsEqualTo(CommandReturnCodes.Success);

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.0.0");
        await Assert.That(tags).Contains("v1.0.0-2");
    }

    [Test]
    public async Task SemverTagFormat_WithoutIterationGroup_RepeatVersionFailsCleanly()
    {
        await SetUpSingleProject("v{major}.{minor}.{patch}", SemverReleaseNameFormat);

        await Assert.That(await AutoVerUtilities.InitializeApp()
                .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.0"]))
            .IsEqualTo(CommandReturnCodes.Success);

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir, "--use-version", "1.0.0"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("v1.0.0");
        await Assert.That(error).Contains("iteration");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    [Test]
    public async Task CustomDateFormat_IsHonoredWhenTagging()
    {
        await SetUpSingleProject("release_{date:yyyyMMdd}[_{iteration}]", "Release {date:yyyyMMdd}[ #{iteration}]");
        await AddChangeFile(IncrementType.Patch, "A fix");

        var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyyMMdd}");
    }

    [Test]
    public async Task InvalidTagFormat_FailsCleanlyWithUserError()
    {
        await SetUpSingleProject("v{major}.{minor}", SemverReleaseNameFormat);

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("TagFormat");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    [Test]
    public async Task TagAndReleaseNameFormatsFromDifferentFamilies_FailCleanlyWithUserError()
    {
        await SetUpSingleProject(SemverTagFormat, "Release {date}");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("ReleaseNameFormat");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    // A version-based tag has no single version to represent once projects can drift apart.
    [Test]
    public async Task SemverTagFormat_WithIndependentlyVersionedProjects_FailsCleanlyWithUserError()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");
        await IOUtilities.CreateProject(_tempDir, "src", "Project2");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project2", "Project2.csproj"), "2.0.0");

        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [
        {{ ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" }},
        {{ ""Name"": ""Project2"", ""Path"": ""src/Project2/Project2.csproj"" }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""ChangeFilesDetermineIncrementType"": true,
    ""TagFormat"": ""{SemverTagFormat}"",
    ""ReleaseNameFormat"": ""{SemverReleaseNameFormat}""
}}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("UseSameVersionForAllProjects");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    [Test]
    public async Task Changelog_UsesTheConfiguredReleaseNameAndTag()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);
        await AddChangeFile(IncrementType.Minor, "A new feature");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var (releaseNameExitCode, releaseName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--release-name"]);
        await Assert.That(releaseNameExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(releaseName.Trim()).IsEqualTo("Release 1.1.0");

        var (tagNameExitCode, tagName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);
        await Assert.That(tagNameExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagName.Trim()).IsEqualTo("v1.1.0");
    }

    // Unrelated tags are common (hand-made tags, tags predating AutoVer). They must neither be
    // mistaken for release history nor provoke a warning - and above all must not reach stdout,
    // which carries values meant for shell capture like TAG=$(autover changelog --tag-name).
    [Test]
    public async Task UnrelatedTags_AreIgnoredWithoutPollutingCapturedOutput()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);
        using (var repo = new Repository(_tempDir))
        {
            repo.ApplyTag("not-a-release");
            repo.ApplyTag("v1.0.0-rc1-extra");
        }

        await AddChangeFile(IncrementType.Minor, "A new feature");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output.Trim()).IsEqualTo("v1.1.0");
        await Assert.That(error).DoesNotContain("Warning");
    }

    // Regression: the first release on a *new* date in a repository that already had tags used to
    // fall through to a tag-shaped release name ("release_2026-09-02") instead of the human-readable
    // "Release 2026-09-02" every other path produced - visible in the release commit message and the
    // GitHub/GitLab release title. Only the no-prior-tags and same-date paths had coverage before.
    [Test]
    public async Task DefaultFormats_FirstReleaseOnANewDate_UsesTheHumanReadableReleaseName()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");
        await IOUtilities.AddAutoVerFile(_tempDir,
            @"{
    ""Projects"": [
        { ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" }
    ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true
}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        // A prior release, on a date that can't be today.
        using (var repo = new Repository(_tempDir))
        {
            repo.ApplyTag("release_2020-01-01");
        }

        await AddChangeFile(IncrementType.Patch, "A fix");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir))
            .IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyy-MM-dd}");
    }

    // Switching an established repository's format orphans every tag written under the old one.
    // The release still has to work, but AutoVer has lost the history it uses to bound a changelog
    // range, so it says so on stderr rather than quietly treating the repo as brand new.
    [Test]
    public async Task ChangingFormatOnAnEstablishedRepo_StillReleasesAndWarnsAboutOrphanedTags()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);
        using (var repo = new Repository(_tempDir))
        {
            repo.ApplyTag("release_2020-01-01");
            repo.ApplyTag("release_2020-01-01_2");
        }

        await AddChangeFile(IncrementType.Patch, "A fix");
        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.1");
        await Assert.That(error).Contains("ignored 2 tag(s)");
        await Assert.That(output).DoesNotContain("ignored");
    }

    // The release commit is created before the tag, so a format git would refuse has to be caught
    // up front - otherwise the repository ends up with a version bump and a release commit but no
    // tag, and a non-zero exit code to untangle. These cases go beyond illegal characters on
    // purpose: git's ref grammar also forbids '..', a trailing '.', a '.lock' suffix, '@{' and an
    // empty path segment, which is why the check defers to git rather than to a list kept here.
    [Test]
    [Arguments("v{major}.{minor}.{patch} rc")]
    [Arguments("v{major}.{minor}.{patch}:{iteration}")]
    [Arguments("v{major}.{minor}.{patch}^")]
    [Arguments("v{major}.{minor}.{patch}[[{iteration}]]")]
    [Arguments("v{major}.{minor}..{patch}")]
    [Arguments("v{major}.{minor}.{patch}.")]
    [Arguments("v{major}.{minor}.{patch}.lock")]
    [Arguments("v{major}.{minor}.{patch}@{{{iteration}}}")]
    [Arguments("v{major}.{minor}.{patch}//{iteration}")]
    public async Task TagFormatGitWouldRefuse_FailsBeforeAnythingIsCommitted(string tagFormat)
    {
        await SetUpSingleProject(tagFormat, SemverReleaseNameFormat);
        await AddChangeFile(IncrementType.Patch, "A fix");

        var commitsBefore = GitUtilities.GetCommitCount(_tempDir);
        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("TagFormat");
        await Assert.That(error).DoesNotContain("at AutoVer.");
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitsBefore);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
        await Assert.That(await IOUtilities.GetProjectVersion(
            Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.0");
    }

    // A format that omits {prerelease} renders 1.0.1-beta and 1.0.1-rc as the same "v1.0.1". That
    // has to be reported as a collision, even though the two versions aren't equal - the tag is what
    // identifies a release, so whatever the format doesn't carry can't be used to tell them apart.
    [Test]
    public async Task SemverTagFormatOmittingPrerelease_TreatsIndistinguishableVersionsAsACollision()
    {
        // --use-version only takes effect when the increment type isn't derived from change files.
        await SetUpSingleProject("v{major}.{minor}.{patch}", SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        await Assert.That(await AutoVerUtilities.InitializeApp()
                .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.1-beta"]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir, "--use-version", "1.0.1-rc"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("v1.0.1");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    // A tag collision has to be detected before the release commit is made, not after it.
    [Test]
    public async Task CollidingTag_FailsBeforeAnythingIsCommitted()
    {
        await SetUpSingleProject("v{major}.{minor}.{patch}", SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        await Assert.That(await AutoVerUtilities.InitializeApp()
                .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.1"]))
            .IsEqualTo(CommandReturnCodes.Success);

        var commitsBefore = GitUtilities.GetCommitCount(_tempDir);
        var (exitCode, _, _) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir, "--use-version", "1.0.1"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitsBefore);
    }

    // --no-tag isn't going to create a tag, so a would-be tag collision must not block it.
    [Test]
    public async Task NoTag_IsNotBlockedByAWouldBeTagCollision()
    {
        await SetUpSingleProject("v{major}.{minor}.{patch}", SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        await Assert.That(await AutoVerUtilities.InitializeApp()
                .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.1"]))
            .IsEqualTo(CommandReturnCodes.Success);

        var commitsBefore = GitUtilities.GetCommitCount(_tempDir);
        var exitCode = await AutoVerUtilities.InitializeApp()
            .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.2", "--no-tag"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetCommitCount(_tempDir)).IsEqualTo(commitsBefore + 1);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).DoesNotContain("v1.0.2");
    }

    // The Dockerfile handler is the path the image repositories use. A version-based tag is rendered
    // from the in-memory project definition after it's written, so this proves the tag carries the
    // new version rather than the one the file held when it was loaded.
    [Test]
    public async Task SemverTagFormat_WithADockerfileProject_TagsTheNewVersion()
    {
        var dockerfilePath = await IOUtilities.CreateDockerfile(_tempDir);
        await IOUtilities.SetDockerfileVersion(dockerfilePath, "1.0.0");

        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [
        {{ ""Name"": ""Image"", ""Path"": ""Dockerfile"" }}
    ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true,
    ""TagFormat"": ""{SemverTagFormat}"",
    ""ReleaseNameFormat"": ""{SemverReleaseNameFormat}""
}}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Image", IncrementType.Minor, "A feature", _tempDir);
        GitUtilities.StageChanges(_tempDir, changeFilePath);
        GitUtilities.CommitChanges(_tempDir, "A feature");

        var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetDockerfileVersion(dockerfilePath)).IsEqualTo("1.1.0");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.1.0");
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo("Release 1.1.0");
    }

    // Setting one option shouldn't require setting the other. TagFormat alone is the obvious first
    // thing a user reaches for, and the release name should follow it rather than clashing with it.
    [Test]
    public async Task SemverTagFormatAlone_DerivesAMatchingReleaseName()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");
        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [
        {{ ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" }}
    ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true,
    ""TagFormat"": ""{SemverTagFormat}""
}}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        await AddChangeFile(IncrementType.Minor, "A new feature");
        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("ReleaseNameFormat");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.1.0");
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo("Release 1.1.0");
    }

    // The commits-based changelog resolves the previous release by looking its tag up in the repo
    // (Tags.First(...)), so the tag AutoVer reports as "last" has to be the tag verbatim. Bounds the
    // range too: the second release's changelog must not re-list the first release's commits.
    [Test]
    public async Task CommitsBasedChangelog_WithSemverTags_CoversOnlyTheLatestRelease()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");
        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [
        {{ ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" }}
    ],
    ""UseCommitsForChangelog"": true,
    ""ChangeFilesDetermineIncrementType"": false,
    ""TagFormat"": ""{SemverTagFormat}""
}}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var readmePath = Path.Combine(_tempDir, "README.md");
        await File.WriteAllTextAsync(readmePath, "# one");
        GitUtilities.StageChanges(_tempDir, readmePath);
        GitUtilities.CommitChanges(_tempDir, "feat: the first feature");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.1");

        await File.WriteAllTextAsync(readmePath, "# two");
        GitUtilities.StageChanges(_tempDir, readmePath);
        GitUtilities.CommitChanges(_tempDir, "feat: the second feature");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.2");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["changelog", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("at AutoVer.");

        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains("the second feature");
        await Assert.That(changelog).DoesNotContain("the first feature");
    }

    // Version ordering answers "which release is highest", not "which release was just cut". After a
    // backport those differ, and a changelog built for the highest tag would describe the wrong
    // release and cover the wrong commit range. The tag on HEAD settles it.
    [Test]
    public async Task BackportRelease_IsReportedAsTheCurrentReleaseDespiteALowerVersion()
    {
        await SetUpSingleProject("v{major}.{minor}.{patch}", SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        foreach (var version in new[] { "1.9.0", "2.0.0", "1.9.1" })
        {
            await Assert.That(await AutoVerUtilities.InitializeApp()
                    .Run(["version", "--project-path", _tempDir, "--use-version", version]))
                .IsEqualTo(CommandReturnCodes.Success);
        }

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v2.0.0");
        await Assert.That(tags).Contains("v1.9.1");

        var (tagExitCode, tagName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);
        await Assert.That(tagExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagName.Trim()).IsEqualTo("v1.9.1");

        var (nameExitCode, releaseName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--release-name"]);
        await Assert.That(nameExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(releaseName.Trim()).IsEqualTo("Release 1.9.1");
    }

    // With ChangeFilesDetermineIncrementType off, `version` rewrites autover.json to reset the
    // increment type. Anything that rewrite drops would silently revert on the next release - so a
    // configured format has to survive the round-trip, and the second release proves it did.
    [Test]
    public async Task ConfigRewrite_PreservesTheConfiguredFormats()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var config = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName));
        await Assert.That(config).Contains(SemverTagFormat);
        await Assert.That(config).Contains(SemverReleaseNameFormat);
        // The computed helpers must not leak into the user's file.
        await Assert.That(config).DoesNotContain("Effective");

        // Still version-based on the release after the rewrite, rather than reverting to dates.
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.0.1");
        await Assert.That(tags).Contains("v1.0.2");
    }

    // Re-releasing the same version bumps only the iteration, and since nothing changed on disk
    // there's no new commit - so both tags end up on the same commit. Picking "the current release"
    // from among several tags on HEAD has to take the latest iteration, not an arbitrary one.
    [Test]
    public async Task SeveralTagsOnTheSameCommit_ReportTheLatestIterationAsCurrent()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        for (var i = 0; i < 2; i++)
        {
            await Assert.That(await AutoVerUtilities.InitializeApp()
                    .Run(["version", "--project-path", _tempDir, "--use-version", "1.0.0"]))
                .IsEqualTo(CommandReturnCodes.Success);
        }

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.0.0");
        await Assert.That(tags).Contains("v1.0.0-2");

        var (exitCode, tagName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagName.Trim()).IsEqualTo("v1.0.0-2");
    }

    // `change` is the command run most often, and it now loads a configuration that validates the
    // tag formats. A configured format must not get in its way.
    [Test]
    public async Task ChangeCommand_WorksWithAConfiguredTagFormat()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["change", "--project-path", _tempDir, "--project-name", "Project1",
                "--increment-type", "Minor", "-m", "A new feature"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(error).DoesNotContain("at AutoVer.");
        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(1);
    }

    // The real release flow is `version` then `changelog` - and `changelog` commits the CHANGELOG
    // itself, moving HEAD off the tagged commit before the release name and tag get read back for the
    // GitHub/GitLab release. So the current release has to be found by walking back from HEAD, not
    // just by looking at HEAD, or a backport reverts to reporting the highest version.
    [Test]
    public async Task BackportRelease_IsStillCurrentAfterTheChangelogCommitMovesHead()
    {
        await SetUpSingleProject("v{major}.{minor}.{patch}", SemverReleaseNameFormat,
            changeFilesDetermineIncrementType: false);

        foreach (var version in new[] { "2.0.0", "1.9.1" })
        {
            await Assert.That(await AutoVerUtilities.InitializeApp()
                    .Run(["version", "--project-path", _tempDir, "--use-version", version]))
                .IsEqualTo(CommandReturnCodes.Success);
        }

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        // The changelog commit is now HEAD, and it carries no tag.
        await Assert.That(GitUtilities.GetLastCommitMessage(_tempDir)).IsEqualTo("Updated changelog");

        var (exitCode, tagName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagName.Trim()).IsEqualTo("v1.9.1");
    }

    // The whole point of version-based tags: the release-branch workflow a shared CI templates repo
    // would use. Cut a release branch, version + changelog on it, merge it back with a merge commit,
    // then read the release name and tag from the merge commit - which is what the sync job does to
    // create the GitHub/GitLab release. This is a branching topology none of the linear tests cover.
    [Test]
    public async Task ReleaseBranchMergedBack_ReportsTheReleaseFromTheMergeCommit()
    {
        await SetUpSingleProject(SemverTagFormat, SemverReleaseNameFormat);
        var defaultBranch = GitUtilities.GetCurrentBranch(_tempDir);

        // An earlier release, so the repo has history to pick the wrong answer from. The changelog
        // run matters: it consumes the change file, so the next release sees only its own.
        await AddChangeFile(IncrementType.Minor, "An earlier feature");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        await AddChangeFile(IncrementType.Patch, "A fix for the next release");

        GitUtilities.CreateAndCheckoutBranch(_tempDir, "releases/next-release");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        GitUtilities.CheckoutBranch(_tempDir, defaultBranch);
        GitUtilities.MergeNoFastForward(_tempDir, "releases/next-release");

        var tags = GitUtilities.GetAllTags(_tempDir);
        await Assert.That(tags).Contains("v1.1.0");
        await Assert.That(tags).Contains("v1.1.1");

        // Reading from the merge commit, exactly as the sync job does.
        var (tagExitCode, tagName, tagError) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--tag-name"]);
        await Assert.That(tagExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(tagError).DoesNotContain("at AutoVer.");
        await Assert.That(tagName.Trim()).IsEqualTo("v1.1.1");

        var (nameExitCode, releaseName, _) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--release-name"]);
        await Assert.That(nameExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(releaseName.Trim()).IsEqualTo("Release 1.1.1");
    }

    // A prerelease label may contain hyphens. The tag is rendered from the parsed project version,
    // so any part of the label lost in parsing would be baked into an immutable tag name.
    [Test]
    public async Task HyphenatedPrereleaseLabel_ReachesTheTagIntact()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");
        await IOUtilities.AddAutoVerFile(_tempDir,
            @"{
    ""Projects"": [
        {
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj"",
            ""PrereleaseLabel"": ""alpha-1""
        }
    ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": false,
    ""TagFormat"": ""v{major}.{minor}.{patch}[-{prerelease}]""
}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);

        var version = await IOUtilities.GetProjectVersion(
            Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"));
        await Assert.That(version).Contains("alpha-1");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"v{version}");
    }

    // Migrating an established repo from the date-based default to version-based tags. The version
    // lives in the project file, not the tag, so the release itself is unaffected; and with change
    // files the changelog is built from those files rather than a commit range, so the orphaned
    // date tags don't affect its contents either.
    [Test]
    public async Task SwitchingFromDateToSemver_ContinuesNormallyWithChangeFiles()
    {
        var csprojPath = await SetUpSingleProject(
            UserConfiguration.DefaultTagFormat, UserConfiguration.DefaultReleaseNameFormat);

        await AddChangeFile(IncrementType.Patch, "The last date-tagged fix");
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        // The switch: same repo, same project file, new tag format. Committed, because `changelog`
        // loads the configuration as of the release tag - an uncommitted switch would leave it
        // reading the old format back and titling the release with a date.
        await RewriteConfigWithFormats(SemverTagFormat, SemverReleaseNameFormat, commit: true);

        await AddChangeFile(IncrementType.Patch, "The first version-tagged fix");
        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.2");
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.2");
        // Said once, on stderr, and only because no version-based history existed yet.
        await Assert.That(error).Contains("ignored");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains("The first version-tagged fix");
        // The previous release's entry is still there - nothing was lost by the switch.
        await Assert.That(changelog).Contains("The last date-tagged fix");

        // From here on it's ordinary: the next release sees the previous version-based one.
        await AddChangeFile(IncrementType.Patch, "A later fix");
        var (nextExitCode, _, nextError) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir]);

        await Assert.That(nextExitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains("v1.0.3");
        // No longer warns, because version-based history now exists.
        await Assert.That(nextError).DoesNotContain("ignored");
    }

    // The one thing the switch costs a commits-based changelog: with no version-based history, the
    // first post-switch release has no previous release to bound its commit range, so it reaches back
    // to the start of the repo. Tagging the previous release's commit with a version-based tag first
    // bridges the gap and keeps the range correct.
    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task SwitchingFromDateToSemver_CommitsChangelogRangeNeedsABridgeTag(
        bool addBridgeTag,
        bool expectOldCommitInChangelog)
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        await IOUtilities.SetProjectVersion(Path.Combine(_tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");
        await RewriteConfigWithFormats(UserConfiguration.DefaultTagFormat, UserConfiguration.DefaultReleaseNameFormat,
            useCommitsForChangelog: true);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var readmePath = Path.Combine(_tempDir, "README.md");
        await File.WriteAllTextAsync(readmePath, "# old");
        GitUtilities.StageChanges(_tempDir, readmePath);
        GitUtilities.CommitChanges(_tempDir, "feat: the date-tagged feature");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        var dateTag = $"release_{DateTime.UtcNow:yyyy-MM-dd}";
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Contains(dateTag);

        await RewriteConfigWithFormats(SemverTagFormat, SemverReleaseNameFormat, useCommitsForChangelog: true);

        if (addBridgeTag)
        {
            // Give the previous release a version-based name on the same commit.
            using var repo = new Repository(_tempDir);
            repo.ApplyTag("v1.0.1", repo.Tags[dateTag].Target.Sha);
        }

        await File.WriteAllTextAsync(readmePath, "# new");
        GitUtilities.StageChanges(_tempDir, readmePath);
        GitUtilities.CommitChanges(_tempDir, "feat: the version-tagged feature");

        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir]))
            .IsEqualTo(CommandReturnCodes.Success);

        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains("the version-tagged feature");
        await Assert.That(changelog.Contains("the date-tagged feature")).IsEqualTo(expectOldCommitInChangelog);
    }

    private async Task RewriteConfigWithFormats(
        string tagFormat,
        string releaseNameFormat,
        bool useCommitsForChangelog = false,
        bool commit = false)
    {
        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [
        {{ ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" }}
    ],
    ""UseCommitsForChangelog"": {useCommitsForChangelog.ToString().ToLowerInvariant()},
    ""ChangeFilesDetermineIncrementType"": {(!useCommitsForChangelog).ToString().ToLowerInvariant()},
    ""TagFormat"": ""{tagFormat}"",
    ""ReleaseNameFormat"": ""{releaseNameFormat}""
}}");

        if (!commit)
            return;

        GitUtilities.StageChanges(_tempDir, Path.Combine(
            ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName));
        GitUtilities.CommitChanges(_tempDir, "Switch to version-based tags");
    }

    private async Task<string> SetUpSingleProject(
        string tagFormat,
        string releaseNameFormat,
        bool changeFilesDetermineIncrementType = true)
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");

        await IOUtilities.AddAutoVerFile(_tempDir,
            $@"{{
    ""Projects"": [
        {{ ""Name"": ""Project1"", ""Path"": ""src/Project1/Project1.csproj"" }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""ChangeFilesDetermineIncrementType"": {changeFilesDetermineIncrementType.ToString().ToLowerInvariant()},
    ""TagFormat"": ""{tagFormat}"",
    ""ReleaseNameFormat"": ""{releaseNameFormat}""
}}");

        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        return csprojPath;
    }

    private async Task AddChangeFile(IncrementType incrementType, string message)
    {
        var changeFilePath = await IOUtilities.AddChangeFile("Project1", incrementType, message, _tempDir);
        GitUtilities.StageChanges(_tempDir, changeFilePath);
        GitUtilities.CommitChanges(_tempDir, message);
    }
}
