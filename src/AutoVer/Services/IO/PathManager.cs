namespace AutoVer.Services.IO;

public class PathManager : IPathManager
{
    private string CurrentDirectory = Directory.GetCurrentDirectory();

    public void SetCurrentDirectory(string? currentDirectory)
    {
        CurrentDirectory = currentDirectory ?? Directory.GetCurrentDirectory();
    }

    public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

    public string GetFullPath(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return Path.GetFullPath(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return Path.GetFullPath(fullPath);
        }
    }

    public string Combine(params string[] paths) => Path.Combine(paths);

    public string? GetExtension(string? path) => Path.GetExtension(path);
}