using AutoVer.Constants;
using AutoVer.IntegrationTests.Utilities;
using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.IntegrationTests;

/// <summary>
/// Automates scenario N: after repeated autover XML edits, the .csproj must still be a
/// well-formed project that `dotnet build` accepts.
/// </summary>
[Retry(3)]
public class ProjectIntegrityTests
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
    public async Task RepeatedVersionBumps_NeverCorruptTheCsproj_StillBuilds()
    {
        await IOUtilities.CreateProject(_tempDir, "src", "Project1");
        var csprojPath = Path.Combine(_tempDir, "src", "Project1", "Project1.csproj");
        await IOUtilities.SetProjectVersion(csprojPath, "1.0.0");

        var autoVerFile =
@"{
    ""Projects"": [
        {
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
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

        foreach (var increment in new[] { IncrementType.Patch, IncrementType.Minor, IncrementType.Major })
        {
            await IOUtilities.AddChangeFile("Project1", increment, $"{increment} change", _tempDir);
            GitUtilities.StageChanges(_tempDir, "*");
            GitUtilities.CommitChanges(_tempDir, $"{increment} change");

            var exitCode = await AutoVerUtilities.InitializeApp().Run(["version", "--project-path", _tempDir]);
            await Assert.That(exitCode).IsEqualTo(CommandReturnCodes.Success);
        }

        await Assert.That(await IOUtilities.GetProjectVersion(csprojPath)).IsEqualTo("2.0.0");

        var buildExitCode = await IOUtilities.BuildProject(csprojPath);
        await Assert.That(buildExitCode).IsEqualTo(0);
    }
}
