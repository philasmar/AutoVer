namespace AutoVer.Services.IO;

public interface IDirectoryManager
{
    void SetCurrentDirectory(string? currentDirectory);
    DirectoryInfo GetDirectoryInfo(string path);
    string[] GetFiles(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly);
    string[] GetDirectories(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly);
    bool Exists(string path);
    DirectoryInfo CreateDirectory(string path);
}