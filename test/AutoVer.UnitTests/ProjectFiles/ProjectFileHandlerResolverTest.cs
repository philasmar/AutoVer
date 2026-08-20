using AutoVer.Exceptions;
using AutoVer.Services.ProjectFiles;
using AutoVer.UnitTests.Utilities;

namespace AutoVer.UnitTests.ProjectFiles;

public class ProjectFileHandlerResolverTest
{
    [Test]
    [Arguments("src/Project1/Project1.csproj", typeof(CsprojNuspecFileHandler))]
    [Arguments("Package.nuspec", typeof(CsprojNuspecFileHandler))]
    [Arguments("src/Project1/Dockerfile", typeof(DockerfileFileHandler))]
    [Arguments("Dockerfile.prod", typeof(DockerfileFileHandler))]
    [Arguments("api.Dockerfile", typeof(DockerfileFileHandler))]
    public async Task Resolve_ReturnsTheHandlerRegisteredForThatFileType(string path, Type expectedHandlerType)
    {
        var resolver = AutoVerUtilities.GetService<IProjectFileHandlerResolver>();
        var handler = resolver.Resolve(path);

        await Assert.That(handler.GetType()).IsEqualTo(expectedHandlerType);
    }

    [Test]
    public async Task Resolve_UnrecognizedFileType_ThrowsInvalidProjectException()
    {
        var resolver = AutoVerUtilities.GetService<IProjectFileHandlerResolver>();

        await Assert.That(() => resolver.Resolve("notes.txt"))
            .Throws<InvalidProjectException>();
    }

    [Test]
    public async Task SearchPatterns_IncludesPatternsFromEveryRegisteredHandler()
    {
        var resolver = AutoVerUtilities.GetService<IProjectFileHandlerResolver>();
        var patterns = resolver.SearchPatterns.ToList();

        await Assert.That(patterns).Contains("*.csproj");
        await Assert.That(patterns).Contains("*.nuspec");
        await Assert.That(patterns).Contains("Dockerfile");
    }
}
