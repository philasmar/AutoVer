using AutoVer.Extensions;
using AutoVer.Services;
using AutoVer.Services.IO;
using Microsoft.Extensions.DependencyInjection;

namespace AutoVer.UnitTests;

/// <summary>
/// Regression coverage for LoadChangeFilesFromRepository resolving its relative
/// ".autover/changes" lookup against the ambient ICurrentDirectoryContext.CurrentDirectory
/// instead of the explicit repositoryRoot parameter it was given. In the real CLI flow this is
/// masked because ConfigurationManager always re-points the shared current directory at the git
/// root before this method ever runs — this test isolates the method from that ambient state
/// entirely, to prove it doesn't depend on some other caller having set it correctly first.
/// </summary>
public class ChangeFileHandlerTest
{
    private string _repoRoot = string.Empty;
    private string _unrelatedDirectory = string.Empty;

    [Before(Test)]
    public void Before()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _unrelatedDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_repoRoot);
        Directory.CreateDirectory(_unrelatedDirectory);
    }

    [After(Test)]
    public void After()
    {
        if (Directory.Exists(_repoRoot))
            Directory.Delete(_repoRoot, true);
        if (Directory.Exists(_unrelatedDirectory))
            Directory.Delete(_unrelatedDirectory, true);
    }

    [Test]
    public async Task LoadChangeFilesFromRepository_AmbientCurrentDirectoryPointsElsewhere_StillFindsChangeFiles()
    {
        var changesDir = Path.Combine(_repoRoot, ".autover", "changes");
        Directory.CreateDirectory(changesDir);
        await File.WriteAllTextAsync(Path.Combine(changesDir, "change.json"),
            """{ "Projects": [ { "Name": "Project1", "Type": "Minor", "ChangelogMessages": ["A change"] } ] }""");

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddCustomServices();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        // Point the shared ambient current directory at a completely unrelated directory —
        // nothing here should ever fall back to it.
        serviceProvider.GetRequiredService<ICurrentDirectoryContext>().SetCurrentDirectory(_unrelatedDirectory);

        var changeFileHandler = serviceProvider.GetRequiredService<IChangeFileHandler>();
        var changeFiles = await changeFileHandler.LoadChangeFilesFromRepository(_repoRoot);

        await Assert.That(changeFiles.Count).IsEqualTo(1);
        await Assert.That(changeFiles[0].Projects[0].Name).IsEqualTo("Project1");
    }
}
