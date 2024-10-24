namespace AutoVer.Services.IO;

public class DirectoryManager : IDirectoryManager
{
    private string CurrentDirectory = Directory.GetCurrentDirectory();

    public void SetCurrentDirectory(string? currentDirectory)
    {
        CurrentDirectory = currentDirectory ?? Directory.GetCurrentDirectory();
    }

    public DirectoryInfo GetDirectoryInfo(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return new DirectoryInfo(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return new DirectoryInfo(fullPath);
        }
    }

    public string[] GetFiles(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (Path.IsPathFullyQualified(path))
            return Directory.GetFiles(path, searchPattern ?? "*", searchOption);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return Directory.GetFiles(fullPath, searchPattern ?? "*", searchOption);
        }
    }

    public string[] GetDirectories(string path, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (Path.IsPathFullyQualified(path))
            return Directory.GetDirectories(path, searchPattern ?? "*", searchOption);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return Directory.GetDirectories(fullPath, searchPattern ?? "*", searchOption);
        }
    }
    
    public bool Exists(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return Directory.Exists(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return Directory.Exists(fullPath);
        }
    }
    
    public DirectoryInfo CreateDirectory(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return Directory.CreateDirectory(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return Directory.CreateDirectory(fullPath);
        }
    }
}