namespace AutoVer.Services.IO;

public class FileManager : IFileManager
{
    public bool Exists(string path) => File.Exists(path);
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(path);
    public Task AppendAllTextAsync(string path, string? contents) => File.AppendAllTextAsync(path, contents);
    public Task WriteAllTextAsync(string path, string? contents) => File.WriteAllTextAsync(path, contents);
    public void Delete(string path) => File.Delete(path);
}