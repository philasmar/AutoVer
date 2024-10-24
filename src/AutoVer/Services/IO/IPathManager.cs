namespace AutoVer.Services.IO;

public interface IPathManager
{
    void SetCurrentDirectory(string? currentDirectory);
    char DirectorySeparatorChar { get; }
    string GetFullPath(string path);
    string Combine(params string[] paths);
    string? GetExtension(string? path);
}