using System.Xml;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;

namespace AutoVer.UnitTests.ProjectFiles;

public class CsprojNuspecFileHandlerTest
{
    private const string CsprojWithVersion =
"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""";

    private const string CsprojWithoutVersion =
"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
""";

    private const string NuspecWithVersion =
"""
<?xml version="1.0"?>
<package>
  <metadata>
    <id>MyPackage</id>
    <version>1.2.3</version>
  </metadata>
</package>
""";

    private const string NuspecWithoutVersion =
"""
<?xml version="1.0"?>
<package>
  <metadata>
    <id>MyPackage</id>
  </metadata>
</package>
""";

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

    private static CsprojNuspecFileHandler CreateHandler() => new(new ThreePartVersionIncrementer(), new FileManager(new CurrentDirectoryContext()));

    [Test]
    [Arguments("Project.csproj", true)]
    [Arguments("PROJECT.CSPROJ", true)]
    [Arguments("Package.nuspec", true)]
    [Arguments("Package.NUSPEC", true)]
    [Arguments("Dockerfile", false)]
    [Arguments("readme.md", false)]
    [Arguments("project.json", false)]
    public async Task IsMatch_MatchesOnlyCsprojAndNuspecExtensions(string fileName, bool expected)
    {
        var handler = CreateHandler();
        await Assert.That(handler.IsMatch(fileName)).IsEqualTo(expected);
    }

    [Test]
    public async Task Load_Csproj_ParsesVersionFromContent()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Project1.csproj", CsprojWithVersion);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");
        await Assert.That(definition.Contents).IsTypeOf<XmlDocument>();
    }

    [Test]
    public async Task Load_Nuspec_ParsesVersionFromContent()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Package.nuspec", NuspecWithVersion);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task Load_Csproj_NoVersionTag_VersionIsNull()
    {
        var handler = CreateHandler();
        var definition = handler.Load("Project1.csproj", CsprojWithoutVersion);

        await Assert.That(definition.Version).IsNull();
    }

    [Test]
    public async Task UpdateVersion_Csproj_DefaultPatchIncrement_PersistsToDisk()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Project1.csproj");
        await File.WriteAllTextAsync(projectPath, CsprojWithVersion);

        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        handler.UpdateVersion(definition, IncrementType.Patch);

        await Assert.That(definition.Version).IsEqualTo("1.2.3");

        var reloaded = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        await Assert.That(reloaded.Version).IsEqualTo("1.2.4");
    }

    [Test]
    public async Task UpdateVersion_Csproj_MajorIncrement_ResetsMinorAndPatch()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Project1.csproj");
        await File.WriteAllTextAsync(projectPath, CsprojWithVersion);

        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        handler.UpdateVersion(definition, IncrementType.Major);

        var reloaded = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        await Assert.That(reloaded.Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task UpdateVersion_Nuspec_PatchIncrement_PersistsToDisk()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Package.nuspec");
        await File.WriteAllTextAsync(projectPath, NuspecWithVersion);

        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        handler.UpdateVersion(definition, IncrementType.Patch);

        var reloaded = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        await Assert.That(reloaded.Version).IsEqualTo("1.2.4");
    }

    [Test]
    public async Task UpdateVersion_OverrideVersion_SetsExactVersion()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Project1.csproj");
        await File.WriteAllTextAsync(projectPath, CsprojWithVersion);

        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "9.9.9");

        var reloaded = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));
        await Assert.That(reloaded.Version).IsEqualTo("9.9.9");
    }

    [Test]
    public async Task UpdateVersion_InvalidOverrideVersion_ThrowsInvalidArgumentException()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Project1.csproj");
        await File.WriteAllTextAsync(projectPath, CsprojWithVersion);
        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));

        await Assert.That(() => handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "not-a-version"))
            .Throws<InvalidArgumentException>();
    }

    [Test]
    public async Task UpdateVersion_NoVersionTag_ThrowsNoVersionTagException()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Project1.csproj");
        await File.WriteAllTextAsync(projectPath, CsprojWithoutVersion);
        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));

        await Assert.That(() => handler.UpdateVersion(definition, IncrementType.Patch))
            .Throws<NoVersionTagException>();
    }

    // The error paths above were only ever exercised against .csproj; GetVersionTagName has a
    // separate branch for .nuspec that needs the same coverage.
    [Test]
    public async Task UpdateVersion_Nuspec_InvalidOverrideVersion_ThrowsInvalidArgumentException()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Package.nuspec");
        await File.WriteAllTextAsync(projectPath, NuspecWithVersion);
        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));

        await Assert.That(() => handler.UpdateVersion(definition, IncrementType.Patch, overrideVersion: "not-a-version"))
            .Throws<InvalidArgumentException>();
    }

    [Test]
    public async Task UpdateVersion_Nuspec_NoVersionTag_ThrowsNoVersionTagException()
    {
        var handler = CreateHandler();
        var projectPath = Path.Combine(_tempDir, "Package.nuspec");
        await File.WriteAllTextAsync(projectPath, NuspecWithoutVersion);
        var definition = handler.Load(projectPath, await File.ReadAllTextAsync(projectPath));

        await Assert.That(() => handler.UpdateVersion(definition, IncrementType.Patch))
            .Throws<NoVersionTagException>();
    }
}
