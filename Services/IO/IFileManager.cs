namespace AutoVer.Services.IO;

public interface IFileManager
{
    bool Exists(string path);
    Task<string> ReadAllTextAsync(string path);
}