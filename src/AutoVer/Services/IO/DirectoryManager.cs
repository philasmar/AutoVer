namespace AutoVer.Services.IO;

public class DirectoryManager : IDirectoryManager
{
    public DirectoryInfo GetDirectoryInfo(string path) => new DirectoryInfo(path);

    public string[] GetFiles(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        => Directory.GetFiles(path, searchPattern ?? "*", searchOption);

    public string[] GetDirectories(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        => Directory.GetDirectories(path, searchPattern ?? "*", searchOption);
    
    public bool Exists(string path) => Directory.Exists(path);
    
    public DirectoryInfo CreateDirectory(string path) => Directory.CreateDirectory(path);
}