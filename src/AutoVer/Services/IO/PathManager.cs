namespace AutoVer.Services.IO;

public class PathManager(ICurrentDirectoryContext currentDirectoryContext) : IPathManager
{
    public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

    public string GetFullPath(string path) =>
        Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, currentDirectoryContext.CurrentDirectory);

    public string Combine(params string[] paths) => Path.Combine(paths);

    public string? GetExtension(string? path) => Path.GetExtension(path);
}
