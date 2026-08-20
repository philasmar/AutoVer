using AutoVer.Services;
using AutoVer.UnitTests.Utilities;

namespace AutoVer.UnitTests;

/// <summary>
/// Regression coverage for a bug where a bare "Dockerfile" was discovered twice: the OS
/// wildcard matcher treats the "Dockerfile.*" search pattern as also matching the
/// extensionless "Dockerfile" itself, and results from different handlers' search patterns
/// were never deduplicated.
/// </summary>
public class ProjectHandlerTest
{
    private string _tempDir = string.Empty;

    [Before(Test)]
    public void Before()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [After(Test)]
    public void After()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public async Task GetAvailableProjects_BareDockerfile_IsDiscoveredExactlyOnce()
    {
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");

        var projectHandler = AutoVerUtilities.GetService<IProjectHandler>();
        var projects = await projectHandler.GetAvailableProjects(_tempDir);

        await Assert.That(projects.Count).IsEqualTo(1);
        await Assert.That(projects[0].ProjectPath).IsEqualTo(dockerfilePath);
    }

    [Test]
    public async Task GetAvailableProjects_DockerfileWithSuffixAndCsproj_EachDiscoveredOnce()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile.prod"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "src", "Project1.csproj"),
            "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");

        var projectHandler = AutoVerUtilities.GetService<IProjectHandler>();
        var projects = await projectHandler.GetAvailableProjects(_tempDir);

        await Assert.That(projects.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetAvailableProjects_IgnoresBackupAndEditorArtifacts()
    {
        var dockerfilePath = Path.Combine(_tempDir, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile.orig"), "FROM alpine:3.20\n");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile.bak"), "FROM alpine:3.20\n");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Dockerfile.swp"), "FROM alpine:3.20\n");

        var projectHandler = AutoVerUtilities.GetService<IProjectHandler>();
        var projects = await projectHandler.GetAvailableProjects(_tempDir);

        await Assert.That(projects.Count).IsEqualTo(1);
        await Assert.That(projects[0].ProjectPath).IsEqualTo(dockerfilePath);
    }

    // Discovery previously deduped via a HashSet<string>, whose enumeration order is an
    // undocumented CLR implementation detail (currently insertion-order for add-only usage,
    // but not a guaranteed contract) rather than an explicit guarantee. Asserting discovery
    // matches a fresh, independent Directory.GetFiles call verifies the real invariant that
    // matters: generated config/changelog output is reproducible because it mirrors actual
    // filesystem enumeration order, not whatever an unordered collection happens to produce.
    [Test]
    public async Task GetAvailableProjects_PreservesFileSystemDiscoveryOrder()
    {
        for (var i = 1; i <= 8; i++)
        {
            var dir = Path.Combine(_tempDir, $"service-{i}");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "Dockerfile"), "FROM alpine:3.20\nLABEL org.opencontainers.image.version=\"1.0.0\"\n");
        }

        // No .csproj/.nuspec/other Dockerfile-suffix files exist in this fixture, so
        // "Dockerfile" is the only search pattern with any matches, and its raw OS
        // enumeration order is exactly what a List-preserving implementation should reproduce.
        var expectedOrder = Directory.GetFiles(_tempDir, "Dockerfile", SearchOption.AllDirectories).ToList();

        var projectHandler = AutoVerUtilities.GetService<IProjectHandler>();
        var actualOrder = (await projectHandler.GetAvailableProjects(_tempDir)).Select(p => p.ProjectPath).ToList();

        await Assert.That(string.Join('|', actualOrder)).IsEqualTo(string.Join('|', expectedOrder));
    }
}
