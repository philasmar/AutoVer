using AutoVer.Models;
using AutoVer.UnitTests.Utilities;
using LibGit2Sharp;

namespace AutoVer.UnitTests;

[Retry(3)]
public class DockerfileVersionTest
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
    public async Task Dockerfile_UseChangeFiles()
    {
        string tempDir = _tempDir;

        await IOUtilities.CreateDockerfile(tempDir);
        await IOUtilities.SetDockerfileVersion(Path.Combine(tempDir, "Dockerfile"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Dockerfile"",
            ""Path"": ""Dockerfile""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Dockerfile", IncrementType.Patch, "Important change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetDockerfileVersion(Path.Combine(tempDir, "Dockerfile"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("Important change");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task CsProjAndDockerfile_MixedProjectTypes_BothHaveChanges()
    {
        string tempDir = _tempDir;

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await IOUtilities.CreateDockerfile(Path.Combine(tempDir, "src", "Project1"));
        await IOUtilities.SetDockerfileVersion(Path.Combine(tempDir, "src", "Project1", "Dockerfile"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project1Image"",
            ""Path"": ""src/Project1/Dockerfile""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Project1 change", tempDir);
        var changeFile2Path = await IOUtilities.AddChangeFile("Project1Image", IncrementType.Minor, "Image change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.StageChanges(tempDir, changeFile2Path);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetDockerfileVersion(Path.Combine(tempDir, "src", "Project1", "Dockerfile"))).IsEqualTo("1.1.0");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");
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
}
