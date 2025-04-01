using AutoVer.Models;
using AutoVer.UnitTests.Utilities;
using LibGit2Sharp;

namespace AutoVer.UnitTests;

[Retry(3)]
public class VersionTest
{
    [Before(Test)]
    public void Before(TestContext context)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        Repository.Init(tempDir);
        using (var repo = new Repository(tempDir))
        {
            context.ObjectBag["tempDir"] = repo.Info.WorkingDirectory;
            IOUtilities.AddGitignore(repo.Info.WorkingDirectory);
        }
    }

    [Test]
    public async Task CsProj_UseChangeFiles()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

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

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Important change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
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
    public async Task CsProj_UseChangeFiles_VersionNoChangedFiles()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

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

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Important change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir, "--use-version", "1.0.0" };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.0");
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
    public async Task CsProj_DontUseChangeFiles_DefaultIncrement()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

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
    ""ChangeFilesDetermineIncrementType"": false
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var readmePath = Path.Combine(tempDir, "README.md");
        await IOUtilities.AddUpdateFile(readmePath, "# Project 1");
        GitUtilities.StageChanges(tempDir, readmePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).DoesNotContain("First change");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    [Arguments(IncrementType.Major)]
    [Arguments(IncrementType.Minor)]
    [Arguments(IncrementType.Patch)]
    [Arguments(IncrementType.None)]
    public async Task CsProj_DontUseChangeFiles_CustomIncrement(IncrementType incrementType)
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

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
    ""ChangeFilesDetermineIncrementType"": false
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", incrementType, "Important change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.0.1)");
        await Assert.That(changelog).Contains("Important change");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task TwoCsProj_UseChangeFiles_AllProjectsHaveChanges()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project2"",
            ""Path"": ""src/Project2/Project2.csproj""
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
        var changeFile2Path = await IOUtilities.AddChangeFile("Project2", IncrementType.Patch, "Project2 change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.StageChanges(tempDir, changeFile2Path);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.0.1)");
        await Assert.That(changelog).Contains("Project1 change");
        await Assert.That(changelog).Contains("### Project2 (1.0.1)");
        await Assert.That(changelog).Contains("Project2 change");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task TwoCsProj_UseChangeFiles_OneProjectHasChanges()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project2"",
            ""Path"": ""src/Project2/Project2.csproj""
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
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.0.0");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.0.1)");
        await Assert.That(changelog).Contains("Project1 change");
        await Assert.That(changelog).DoesNotContain("### Project2 (1.0.1)");
        await Assert.That(changelog).DoesNotContain("### Project2 (1.0.0)");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task TwoCsProj_DontUseChangeFiles_AllProjectsHaveChanges()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project2"",
            ""Path"": ""src/Project2/Project2.csproj""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": false
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var readmePath = Path.Combine(tempDir, "README.md");
        await IOUtilities.AddUpdateFile(readmePath, "# Project 1");
        GitUtilities.StageChanges(tempDir, readmePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).DoesNotContain("### Project1 (1.0.1)");
        await Assert.That(changelog).DoesNotContain("### Project2 (1.0.1)");
        await Assert.That(changelog).DoesNotContain("First change");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    [Arguments(IncrementType.Major)]
    [Arguments(IncrementType.Minor)]
    [Arguments(IncrementType.Patch)]
    [Arguments(IncrementType.None)]
    public async Task TwoCsProj_DontUseChangeFiles_AllProjectsHaveChanges_CustomIncrement(IncrementType incrementType)
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project2"",
            ""Path"": ""src/Project2/Project2.csproj""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": false,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": false
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", incrementType, "Project1 change", tempDir);
        var changeFile2Path = await IOUtilities.AddChangeFile("Project2", incrementType, "Project2 change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.StageChanges(tempDir, changeFile2Path);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.0.1)");
        await Assert.That(changelog).Contains("Project1 change");
        await Assert.That(changelog).Contains("### Project2 (1.0.1)");
        await Assert.That(changelog).Contains("Project2 change");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task TwoCsProj_OneContainer_UseChangeFiles_OneProjectHasChanges()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Paths"": [
                ""src/Project1/Project1.csproj"",
                ""src/Project2/Project2.csproj""
            ]
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
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.0.1)");
        await Assert.That(changelog).Contains("Project1 change");
        await Assert.That(changelog).DoesNotContain("### Project2 (1.0.1)");
        await Assert.That(changelog).DoesNotContain("### Project2 (1.0.0)");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task TwoCsProj_UseSameVersionForAllProjects_OneProjectHasChanges_ProjectsHaveSameVersion()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.0.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project2"",
            ""Path"": ""src/Project2/Project2.csproj""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": true,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Project1 change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.0.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.0.1)");
        await Assert.That(changelog).Contains("Project1 change");
        await Assert.That(changelog).Contains("### Project2 (1.0.1)");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [Test]
    public async Task TwoCsProj_UseSameVersionForAllProjects_OneProjectHasChanges_ProjectsHaveDifferentVersions()
    {
        string tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString() ?? throw new Exception("Temp directory is null");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project1")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"), "1.0.0");

        await Assert.That(await IOUtilities.CreateProject(tempDir, "src", "Project2")).IsTrue();
        await IOUtilities.SetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"), "1.1.0");

        string autoVerFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""Project1"",
            ""Path"": ""src/Project1/Project1.csproj""
        }},
        {{
            ""Name"": ""Project2"",
            ""Path"": ""src/Project2/Project2.csproj""
        }}
    ],
    ""UseCommitsForChangelog"": false,
    ""UseSameVersionForAllProjects"": true,
    ""DefaultIncrementType"": ""Patch"",
    ""ChangeFilesDetermineIncrementType"": true
}}";

        var autoVerFilePath = IOUtilities.AddAutoVerFile(tempDir, autoVerFile);
        GitUtilities.StageChanges(tempDir, "*");
        GitUtilities.CommitChanges(tempDir, "Initial Commit");

        var changeFilePath = await IOUtilities.AddChangeFile("Project1", IncrementType.Patch, "Project1 change", tempDir);
        GitUtilities.StageChanges(tempDir, changeFilePath);
        GitUtilities.CommitChanges(tempDir, "First change");

        var app = AutoVerUtilities.InitializeApp();
        await Assert.That(app).IsNotNull();

        var versionArgs = new[] { "version", "--project-path", tempDir };
        var exitCode = await app!.Run(versionArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project1", "Project1.csproj"))).IsEqualTo("1.1.1");
        await Assert.That(await IOUtilities.GetProjectVersion(Path.Combine(tempDir, "src", "Project2", "Project2.csproj"))).IsEqualTo("1.1.1");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(GitUtilities.GetLastTag(tempDir)).IsEqualTo($"release_{DateTime.UtcNow:yyyy-MM-dd}");

        app = AutoVerUtilities.InitializeApp();
        var changelogArgs = new[] { "changelog", "--project-path", tempDir };
        exitCode = await app!.Run(changelogArgs);

        await Assert.That(exitCode).IsEqualTo(0);
        var changelog = await IOUtilities.GetChangelog(tempDir);
        await Assert.That(changelog).Contains($"Release {DateTime.UtcNow:yyyy-MM-dd}");
        await Assert.That(changelog).Contains("### Project1 (1.1.1)");
        await Assert.That(changelog).Contains("Project1 change");
        await Assert.That(changelog).Contains("### Project2 (1.1.1)");
        await Assert.That(GitUtilities.GetLastCommitMessage(tempDir)).IsEqualTo("Updated changelog");
    }

    [After(Test)]
    public void After()
    {
        try
        {
            var tempDir = TestContext.Current?.ObjectBag["tempDir"]?.ToString();
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                IOUtilities.RemoveReadOnly(tempDir);
                Directory.Delete(tempDir, true);
            }
        }
        catch (Exception ex)
        {
            Assert.Fail(ex.Message);
        }
    }
}