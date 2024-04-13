namespace AutoVer.Services.IO;

public interface IPathManager
{
    char DirectorySeparatorChar { get; }
    string Combine(params string[] paths);
    string? GetExtension(string? path);
}