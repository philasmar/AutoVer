namespace AutoVer.Services.IO;

public class FileManager(ICurrentDirectoryContext currentDirectoryContext) : IFileManager
{
    private string ResolveFullPath(string path) =>
        Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, currentDirectoryContext.CurrentDirectory);

    public bool Exists(string path) => File.Exists(ResolveFullPath(path));

    public string ReadAllText(string path) => File.ReadAllText(ResolveFullPath(path));

    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(ResolveFullPath(path));

    public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(ResolveFullPath(path));

    public Task AppendAllTextAsync(string path, string? contents) => File.AppendAllTextAsync(ResolveFullPath(path), contents);

    public void WriteAllText(string path, string? contents) => File.WriteAllText(ResolveFullPath(path), contents);

    public Task WriteAllTextAsync(string path, string? contents) => File.WriteAllTextAsync(ResolveFullPath(path), contents);

    public Stream OpenWrite(string path) => File.Create(ResolveFullPath(path));

    public void Delete(string path) => File.Delete(ResolveFullPath(path));
}
