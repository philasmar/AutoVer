namespace AutoVer.Services.IO;

public interface IFileManager
{
    void SetCurrentDirectory(string? currentDirectory);
    bool Exists(string path);
    string ReadAllText(string path);
    Task<string> ReadAllTextAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task AppendAllTextAsync(string path, string? contents);
    Task WriteAllTextAsync(string path, string? contents);
    void Delete(string path);
}