namespace AutoVer.Services.IO;

public class PathManager : IPathManager
{
    public string Combine(params string[] paths) => Path.Combine(paths);
}