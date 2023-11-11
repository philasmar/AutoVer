namespace AutoVer.Services.IO;

public interface IDirectoryManager
{
    DirectoryInfo GetDirectoryInfo(string path);
    string[] GetFiles(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly);
    bool Exists(string path);
}