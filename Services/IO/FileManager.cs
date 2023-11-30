namespace AutoVer.Services.IO;

public class FileManager : IFileManager
{
    public bool Exists(string path) => File.Exists(path);
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(path);
}