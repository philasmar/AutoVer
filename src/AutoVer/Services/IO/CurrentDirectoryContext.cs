namespace AutoVer.Services.IO;

public class CurrentDirectoryContext : ICurrentDirectoryContext
{
    public string CurrentDirectory { get; private set; } = Directory.GetCurrentDirectory();

    public void SetCurrentDirectory(string? currentDirectory)
    {
        // Path.GetFullPath(path, basePath) later requires basePath to already be fully
        // qualified, so a relative value (e.g. ".", "src/project1") must be resolved against
        // the real process working directory right here, not stored as-is.
        CurrentDirectory = string.IsNullOrEmpty(currentDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(currentDirectory);
    }
}
