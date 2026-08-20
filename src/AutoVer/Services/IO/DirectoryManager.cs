namespace AutoVer.Services.IO;

public class DirectoryManager(ICurrentDirectoryContext currentDirectoryContext) : IDirectoryManager
{
    private string ResolveFullPath(string path) =>
        Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, currentDirectoryContext.CurrentDirectory);

    public DirectoryInfo GetDirectoryInfo(string path) => new(ResolveFullPath(path));

    public string[] GetFiles(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.GetFiles(ResolveFullPath(path), searchPattern ?? "*", searchOption);

    public string[] GetDirectories(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.GetDirectories(ResolveFullPath(path), searchPattern ?? "*", searchOption);

    public bool Exists(string path) => Directory.Exists(ResolveFullPath(path));

    public DirectoryInfo CreateDirectory(string path) => Directory.CreateDirectory(ResolveFullPath(path));
}
