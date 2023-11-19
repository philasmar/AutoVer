using AutoVer.Services.IO;
using LibGit2Sharp;

namespace AutoVer.Services;

public class GitHandler(
    IDirectoryManager directoryManager) : IGitHandler
{
    public string FindGitRootDirectory(string currentPath)
    {
        var currentDir = currentPath;
        while (currentDir != null)
        {
            if (directoryManager.GetDirectories(currentDir, ".git").Any())
            {
                var sourceControlRootDirectory = directoryManager.GetDirectoryInfo(currentDir).FullName;
                return sourceControlRootDirectory;
            }

            currentDir = directoryManager.GetDirectoryInfo(currentDir).Parent?.FullName;
        }

        return string.Empty;
    }

    public TagCollection GetGitTags(string currentPath)
    {
        var gitRoot = FindGitRootDirectory(currentPath);
        using var gitRepository = new Repository(gitRoot);
        return gitRepository.Tags;
    }
}