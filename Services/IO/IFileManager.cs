namespace AutoVer.Services.IO;

public interface IFileManager
{
    bool Exists(string path);
    Task<string> ReadAllTextAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task AppendAllTextAsync(string path, string? contents);
    Task WriteAllTextAsync(string path, string? contents);
}