namespace AutoVer.Services.IO;

public class FileManager : IFileManager
{
    private string CurrentDirectory = Directory.GetCurrentDirectory();

    public void SetCurrentDirectory(string? currentDirectory)
    {
        CurrentDirectory = currentDirectory ?? Directory.GetCurrentDirectory();
    }

    public bool Exists(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return File.Exists(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return File.Exists(fullPath);
        }
    }

    public string ReadAllText(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return File.ReadAllText(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return File.ReadAllText(fullPath);
        }
    }

    public Task<string> ReadAllTextAsync(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return File.ReadAllTextAsync(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return File.ReadAllTextAsync(fullPath);
        }
    }

    public Task<byte[]> ReadAllBytesAsync(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return File.ReadAllBytesAsync(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return File.ReadAllBytesAsync(fullPath);
        }
    }

    public Task AppendAllTextAsync(string path, string? contents)
    {
        if (Path.IsPathFullyQualified(path))
            return File.AppendAllTextAsync(path, contents);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return File.AppendAllTextAsync(fullPath, contents);
        }
    }

    public Task WriteAllTextAsync(string path, string? contents)
    {
        if (Path.IsPathFullyQualified(path))
            return File.WriteAllTextAsync(path, contents);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            return File.WriteAllTextAsync(fullPath, contents);
        }
    }

    public void Delete(string path)
    {
        if (Path.IsPathFullyQualified(path))
            File.Delete(path);
        else
        {
            var fullPath = Path.GetFullPath(path, CurrentDirectory);
            File.Delete(fullPath);
        }
    }
}