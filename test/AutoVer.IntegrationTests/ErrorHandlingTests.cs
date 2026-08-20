using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// Automates scenario M: `autover change` error-handling sanity checks. Both an unknown
/// --project-name and an invalid --increment-type are hard failures (UserError exit code)
/// with a clean, user-facing message — no silent fallback, no raw stack trace.
/// </summary>
[Retry(3)]
public class ErrorHandlingTests
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

    [Test]
    public async Task Change_UnknownProjectName_FailsCleanlyWithUserError()
    {
        await SetUpProjectWithChangeFileConfig("Project1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["change", "--project-path", _tempDir, "--project-name", "DoesNotExist", "--increment-type", "Patch", "-m", "test"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("DoesNotExist");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    [Test]
    public async Task Change_InvalidIncrementType_FailsCleanlyWithUserError()
    {
        await SetUpProjectWithChangeFileConfig("Project1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["change", "--project-path", _tempDir, "--project-name", "Project1", "--increment-type", "NotARealIncrementType", "-m", "test"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("NotARealIncrementType");
        await Assert.That(error).DoesNotContain("at AutoVer.");
        await Assert.That(IOUtilities.GetChangeFileCount(_tempDir)).IsEqualTo(0);
    }

    [Test]
    public async Task Version_InvalidIncrementType_FailsCleanlyWithUserError()
    {
        await SetUpProjectWithChangeFileConfig("Project1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["version", "--project-path", _tempDir, "--increment-type", "NotARealIncrementType"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("NotARealIncrementType");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    [Test]
    public async Task Changelog_InvalidIncrementType_FailsCleanlyWithUserError()
    {
        await SetUpProjectWithChangeFileConfig("Project1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(
            ["changelog", "--project-path", _tempDir, "--increment-type", "NotARealIncrementType"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("NotARealIncrementType");
        await Assert.That(error).DoesNotContain("at AutoVer.");
    }

    // Without --verbose, an expected (AutoVerException) failure only ever shows the
    // top-level message — the inner exception it wraps (here, the real JSON parse error)
    // is never surfaced.
    [Test]
    public async Task CorruptConfig_WithoutVerbose_HidesInnerExceptionAndStackTrace()
    {
        await SetUpProjectWithCorruptConfig("Project1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("There was an issue loading the user configuration");
        await Assert.That(error).DoesNotContain("at AutoVer.");
        await Assert.That(error).DoesNotContain("System.Text.Json");
    }

    // With --verbose, the same failure surfaces the full stack trace AND the wrapped inner
    // exception (the actual JSON parse error), which is otherwise completely invisible.
    [Test]
    public async Task CorruptConfig_WithVerbose_ShowsStackTraceAndInnerException()
    {
        await SetUpProjectWithCorruptConfig("Project1");

        var (exitCode, _, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir, "--verbose"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.UserError);
        await Assert.That(error).Contains("There was an issue loading the user configuration");
        await Assert.That(error).Contains("at AutoVer.Services.ConfigurationManager");
        await Assert.That(error).Contains("System.Text.Json");
    }

    private async Task SetUpProjectWithCorruptConfig(string projectName)
    {
        await IOUtilities.CreateProject(_tempDir, "src", projectName);
        var csprojPath = Path.Combine(_tempDir, "src", projectName, $"{projectName}.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");

        await IOUtilities.AddAutoVerFile(_tempDir, "{ this is not valid json");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");
    }

    private async Task SetUpProjectWithChangeFileConfig(string projectName)
    {
        await IOUtilities.CreateProject(_tempDir, "src", projectName);
        var csprojPath = Path.Combine(_tempDir, "src", projectName, $"{projectName}.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");

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
    }
}
