namespace AutoVer.Services.IO;

public interface IPathManager
{
    string Combine(params string[] paths);
}