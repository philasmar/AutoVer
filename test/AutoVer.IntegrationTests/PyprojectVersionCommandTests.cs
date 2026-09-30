using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// The pyproject.toml handler through the full CLI: `autover version` bumps, commits and tags a
/// Python project listed in autover.json, seeds one that has no version, and never auto-discovers a
/// pyproject.toml on its own.
/// </summary>
[Retry(3)]
public class PyprojectVersionCommandTests
{
    private const string Pyproject =
"""
[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"

[project]
name = "walleye-datalake"
version = "1.0.0"
dependencies = [
  "requests>=2.28",
]

[tool.pytest.ini_options]
testpaths = ["tests"]
""";

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

    private async Task<string> SetUpRepo(string pyproject, string extraConfig = "")
    {
        var path = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(path, pyproject);
        await IOUtilities.AddAutoVerFile(_tempDir,
$@"{{
    ""Projects"": [ {{ ""Name"": ""walleye-datalake"", ""Path"": ""pyproject.toml"" }} ],
    ""UseCommitsForChangelog"": false,
    ""ChangeFilesDetermineIncrementType"": true{extraConfig}
}}");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");
        return path;
    }

    [Test]
    public async Task ChangeFile_BumpsTheVersionOnly_CommitsAndTags()
    {
        var path = await SetUpRepo(Pyproject);
        await IOUtilities.AddChangeFile("walleye-datalake", IncrementType.Minor, "A feature", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(Pyproject.Replace("version = \"1.0.0\"", "version = \"1.1.0\""));
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).Count().IsEqualTo(1);
        using var repo = new Repository(_tempDir);
        await Assert.That(repo.RetrieveStatus().IsDirty).IsFalse();
    }

    [Test]
    public async Task VersionCurrent_PrintsThePyprojectVersion()
    {
        await SetUpRepo(Pyproject);

        var (exitCode, output, _) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir, "--current"]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(output.Trim()).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task NoVersion_IsSeededWithInitialVersion()
    {
        var path = await SetUpRepo(Pyproject.Replace("version = \"1.0.0\"\n", ""), ",\n    \"InitialVersion\": \"0.3.0\"");
        await IOUtilities.AddChangeFile("walleye-datalake", IncrementType.Patch, "First release", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await File.ReadAllTextAsync(path)).Contains("name = \"walleye-datalake\"\nversion = \"0.3.0\"\n");
    }

    [Test]
    public async Task DynamicVersion_FailsWithoutTouchingTheRepository()
    {
        var dynamic = Pyproject.Replace("version = \"1.0.0\"", "dynamic = [\"version\"]");
        var path = await SetUpRepo(dynamic);

        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsNotEqualTo(CommandReturnCodes.Success);
        await Assert.That(output + error).Contains("dynamic");
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(dynamic);
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // Many non-Python repositories keep a tool-config-only pyproject.toml; relying on discovery must
    // never start versioning it.
    [Test]
    public async Task NoConfig_PyprojectIsNotAutoDiscovered()
    {
        var path = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(path, Pyproject);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsNotEqualTo(CommandReturnCodes.Success);
        await Assert.That(output + error).Contains("only versioned when listed in .autover/autover.json");
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(Pyproject);
    }

    [Test]
    public async Task SharedVersion_WithACsproj_MovesBothTogether()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");
        var pyprojectPath = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(pyprojectPath, Pyproject);
        await IOUtilities.AddAutoVerFile(_tempDir,
"""
{
    "Projects": [
        { "Name": "Project1", "Path": "src/Project1/Project1.csproj" },
        { "Name": "walleye-datalake", "Path": "pyproject.toml" }
    ],
    "UseCommitsForChangelog": false,
    "UseSameVersionForAllProjects": true,
    "ChangeFilesDetermineIncrementType": true
}
""");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");
        await IOUtilities.AddChangeFile("walleye-datalake", IncrementType.Major, "Breaking", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Change");

        var app = AutoVerUtilities.InitializeApp();
        var exitCode = await app.Run(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("2.0.0");
        await Assert.That(await File.ReadAllTextAsync(pyprojectPath)).Contains("version = \"2.0.0\"");
    }

    [Test]
    public async Task Changelog_AfterVersion_RecordsThePythonRelease()
    {
        await SetUpRepo(Pyproject);
        await IOUtilities.AddChangeFile("walleye-datalake", IncrementType.Patch, "Fix the thing", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Change");

        // A fresh app per command, as a CLI invocation is.
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir])).IsEqualTo(CommandReturnCodes.Success);
        await Assert.That(await AutoVerUtilities.InitializeApp().Run(["changelog", "--project-path", _tempDir])).IsEqualTo(CommandReturnCodes.Success);

        var changelog = await IOUtilities.GetChangelog(_tempDir);
        await Assert.That(changelog).Contains("walleye-datalake (1.0.1)");
        await Assert.That(changelog).Contains("Fix the thing");
    }

    // Review finding: a label pip rejects used to fail only when the pyproject's turn came,
    // leaving the .csproj listed before it bumped and staged.
    [Test]
    public async Task VersionPipRejects_FailsBeforeAnyFileIsWritten()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");
        var pyprojectPath = Path.Combine(_tempDir, "pyproject.toml");
        await File.WriteAllTextAsync(pyprojectPath, Pyproject);
        await IOUtilities.AddAutoVerFile(_tempDir,
"""
{
    "Projects": [
        { "Name": "Project1", "Path": "src/Project1/Project1.csproj" },
        { "Name": "walleye-datalake", "Path": "pyproject.toml" }
    ],
    "UseCommitsForChangelog": false,
    "ChangeFilesDetermineIncrementType": false
}
""");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir, "--use-version", "2.0.0-hotfix"]);

        await Assert.That(exitCode).IsNotEqualTo(CommandReturnCodes.Success);
        await Assert.That(output + error).Contains("PEP 440");
        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("1.0.0");
        await Assert.That(await File.ReadAllTextAsync(pyprojectPath)).IsEqualTo(Pyproject);
        using var repo = new Repository(_tempDir);
        await Assert.That(repo.RetrieveStatus().IsDirty).IsFalse();
        await Assert.That(GitUtilities.GetAllTags(_tempDir)).IsEmpty();
    }

    // Review finding: this used to crash with "Unhandled exception. This is a bug." because the
    // version is parsed before the handler is ever asked to write it.
    [Test]
    public async Task NormalizedPythonSpelling_IsAnExplainedErrorNotACrash()
    {
        await SetUpRepo(Pyproject.Replace("version = \"1.0.0\"", "version = \"1.0.0b1\""));
        await IOUtilities.AddChangeFile("walleye-datalake", IncrementType.Patch, "x", _tempDir);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Change");

        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsNotEqualTo(CommandReturnCodes.Success);
        await Assert.That(exitCode).IsNotEqualTo(CommandReturnCodes.UnhandledException);
        await Assert.That(output + error).Contains("1.0.0-b1");
    }

    // The configuration-loading error names the cause - and, for a bad setting, keeps the JSON
    // path System.Text.Json reports rather than only its innermost token-type complaint.
    [Test]
    public async Task InvalidSetting_ErrorKeepsTheJsonPath()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "pyproject.toml"), Pyproject);
        await IOUtilities.AddAutoVerFile(_tempDir,
"""
{ "Projects": [ { "Name": "walleye-datalake", "Path": "pyproject.toml" } ], "UseSameVersionForAllProjects": "yes" }
""");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial Commit");

        var (exitCode, output, error) = await AutoVerUtilities.RunCapturingOutput(["version", "--project-path", _tempDir]);

        await Assert.That(exitCode).IsNotEqualTo(CommandReturnCodes.Success);
        await Assert.That(output + error).Contains("UseSameVersionForAllProjects");
    }
}
