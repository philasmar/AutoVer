using AutoVer.Models;
using AutoVer.Services;
using AutoVer.UnitTests.Utilities;
using LibGit2Sharp;

namespace AutoVer.UnitTests;

/// <summary>
/// Regression coverage for a bug where auto-discovered Dockerfiles in different folders
/// (which all share the generic file name "Dockerfile") collided on the same generated
/// project name.
/// </summary>
public class ConfigurationManagerTest
{
    private string _tempDir = string.Empty;

    [Before(Test)]
    public void Before()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        Repository.Init(_tempDir);
    }

    [After(Test)]
    public void After()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public async Task RetrieveUserConfiguration_MultipleDockerfilesSameName_GetDistinctProjectNames()
    {
        var serviceADir = Path.Combine(_tempDir, "service-a");
        var serviceBDir = Path.Combine(_tempDir, "service-b");
        Directory.CreateDirectory(serviceADir);
        Directory.CreateDirectory(serviceBDir);
        await File.WriteAllTextAsync(Path.Combine(serviceADir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        await File.WriteAllTextAsync(Path.Combine(serviceBDir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");

        var configurationManager = AutoVerUtilities.GetService<IConfigurationManager>();
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(_tempDir, IncrementType.Patch);

        await Assert.That(userConfiguration.Projects.Count).IsEqualTo(2);
        var names = userConfiguration.Projects.Select(p => p.Name).ToList();
        await Assert.That(names.Distinct().Count()).IsEqualTo(2);
        await Assert.That(names).Contains("service-a-Dockerfile");
        await Assert.That(names).Contains("service-b-Dockerfile");
    }

    // A single level of disambiguation (immediate parent dir only) isn't always enough: two
    // projects can share the same immediate parent directory name too (e.g. teamA/service and
    // teamB/service). Names must keep escalating until they're actually unique.
    [Test]
    public async Task RetrieveUserConfiguration_CollidingParentDirectoryNamesToo_StillGetDistinctNames()
    {
        var teamADir = Path.Combine(_tempDir, "teamA", "service");
        var teamBDir = Path.Combine(_tempDir, "teamB", "service");
        Directory.CreateDirectory(teamADir);
        Directory.CreateDirectory(teamBDir);
        await File.WriteAllTextAsync(Path.Combine(teamADir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        await File.WriteAllTextAsync(Path.Combine(teamBDir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");

        var configurationManager = AutoVerUtilities.GetService<IConfigurationManager>();
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(_tempDir, IncrementType.Patch);

        await Assert.That(userConfiguration.Projects.Count).IsEqualTo(2);
        var names = userConfiguration.Projects.Select(p => p.Name).ToList();
        await Assert.That(names.Distinct().Count()).IsEqualTo(2);
    }

    // The '-'-joined directory-segment prefix is theoretically ambiguous ("foo/bar-baz" and
    // "foo-bar/baz" both join to "foo-bar-baz"), which is why GetUniqueProjectNames has a
    // guaranteed-unique full-path fallback for whatever might still collide once every segment
    // is exhausted. In practice this particular pair resolves at depth 1 already (folder names
    // are compared as whole strings before any joining happens, so "bar-baz" != "baz"
    // immediately) — constructing real directory names that survive escalation all the way to
    // the fallback turns out to be very hard, which is itself a useful data point. This test
    // instead locks in the property that actually matters end-to-end: hyphenated directory
    // names disambiguate correctly, and an unrelated, already-unique project's friendly name
    // is never touched by another pair's collision handling.
    [Test]
    public async Task RetrieveUserConfiguration_HyphenatedDirectoryNames_DisambiguateWithoutDisturbingUnrelatedProject()
    {
        var pathA = Path.Combine(_tempDir, "foo", "bar-baz");
        var pathB = Path.Combine(_tempDir, "foo-bar", "baz");
        Directory.CreateDirectory(pathA);
        Directory.CreateDirectory(pathB);
        await File.WriteAllTextAsync(Path.Combine(pathA, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        await File.WriteAllTextAsync(Path.Combine(pathB, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");

        Directory.CreateDirectory(Path.Combine(_tempDir, "src", "UniqueProject"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "src", "UniqueProject", "UniqueProject.csproj"),
            "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");

        var configurationManager = AutoVerUtilities.GetService<IConfigurationManager>();
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(_tempDir, IncrementType.Patch);

        await Assert.That(userConfiguration.Projects.Count).IsEqualTo(3);
        var names = userConfiguration.Projects.Select(p => p.Name).ToList();
        await Assert.That(names.Distinct().Count()).IsEqualTo(3);

        // The unrelated, already-unique project must be completely unaffected by the other
        // two projects' collision and fallback.
        await Assert.That(names).Contains("UniqueProject");
    }

    [Test]
    public async Task RetrieveUserConfiguration_SingleDockerfile_KeepsPlainName()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");

        var configurationManager = AutoVerUtilities.GetService<IConfigurationManager>();
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(_tempDir, IncrementType.Patch);

        await Assert.That(userConfiguration.Projects.Count).IsEqualTo(1);
        await Assert.That(userConfiguration.Projects[0].Name).IsEqualTo("Dockerfile");
    }

    // An existing autover.json that simply omits "Projects" deserializes to an empty (not
    // null) list, per UserConfiguration.Projects' default. That took the discovery branch,
    // but GitRoot was only ever backfilled inside the "userConfiguration is null" case, which
    // this isn't — leaving GitRoot at its [JsonIgnore] default of "" and failing the final
    // git-repository check even though the repo and discovery both succeeded.
    [Test]
    public async Task RetrieveUserConfiguration_ExistingConfigWithNoProjectsKey_StillBackfillsGitRootAndDiscovers()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        var autoverDir = Path.Combine(_tempDir, ".autover");
        Directory.CreateDirectory(autoverDir);
        await File.WriteAllTextAsync(Path.Combine(autoverDir, "autover.json"),
            """{ "UseCommitsForChangelog": false, "UseSameVersionForAllProjects": false, "DefaultIncrementType": "Patch", "ChangeFilesDetermineIncrementType": false }""");

        var configurationManager = AutoVerUtilities.GetService<IConfigurationManager>();
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(_tempDir, IncrementType.Patch);

        // Asserting non-empty (rather than an exact match against _tempDir) since GitRoot comes
        // back through DirectoryInfo/FullName, which can normalize a symlinked temp path (e.g.
        // macOS's /var -> /private/var) differently than the raw string this test started with;
        // what the bug actually left broken was GitRoot being empty, not a specific casing of it.
        await Assert.That(string.IsNullOrEmpty(userConfiguration.GitRoot)).IsFalse();
        await Assert.That(userConfiguration.Projects.Count).IsEqualTo(1);
        await Assert.That(userConfiguration.Projects[0].Name).IsEqualTo("Dockerfile");
    }

    // RetrieveUserConfiguration(..., tagName) loads the config as it existed AT that tag, so
    // ChangelogCommand can see the settings that were in effect for that release. But a config
    // can be added/edited after a tag already exists (e.g. adopting change files on a repo that
    // already has releases) - in that case the tag-scoped lookup legitimately finds nothing,
    // and must fall back to the current on-disk config rather than silently reverting to
    // UserConfiguration's bare defaults (which would wrongly flip UseCommitsForChangelog back
    // to true).
    [Test]
    public async Task RetrieveUserConfiguration_ConfigAddedAfterTag_StillUsesCurrentConfigSettings()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Initial commit");
        using (var repo = new Repository(_tempDir))
            repo.ApplyTag("release_2026-08-20");

        var autoverDir = Path.Combine(_tempDir, ".autover");
        Directory.CreateDirectory(autoverDir);
        await File.WriteAllTextAsync(Path.Combine(autoverDir, "autover.json"),
            """
            {
              "Projects": [ { "Name": "Dockerfile", "Path": "Dockerfile" } ],
              "UseCommitsForChangelog": false,
              "UseSameVersionForAllProjects": false,
              "DefaultIncrementType": "Patch",
              "ChangeFilesDetermineIncrementType": false
            }
            """);
        GitUtilities.StageChanges(_tempDir, "*");
        GitUtilities.CommitChanges(_tempDir, "Add autover.json after the tag");

        var configurationManager = AutoVerUtilities.GetService<IConfigurationManager>();
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(_tempDir, IncrementType.Patch, "release_2026-08-20");

        await Assert.That(userConfiguration.UseCommitsForChangelog).IsFalse();
        await Assert.That(userConfiguration.Projects.Count).IsEqualTo(1);
        await Assert.That(userConfiguration.Projects[0].Name).IsEqualTo("Dockerfile");
    }
}
