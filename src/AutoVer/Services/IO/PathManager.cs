namespace AutoVer.Services.IO;

public class PathManager : IPathManager
{
    public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

    public string Combine(params string[] paths) => Path.Combine(paths);

    public string? GetExtension(string? path) => Path.GetExtension(path);
}