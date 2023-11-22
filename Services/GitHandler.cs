using AutoVer.Exceptions;
using AutoVer.Services.IO;
using LibGit2Sharp;

namespace AutoVer.Services;

public class GitHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager) : IGitHandler
{
    public string FindGitRootDirectory(string? currentPath)
    {
        if (string.IsNullOrEmpty(currentPath))
            throw new InvalidProjectPathException($"The provided project path is empty or invalid.");
        
        if (fileManager.Exists(currentPath))
        {
            currentPath = directoryManager.GetDirectoryInfo(currentPath).Parent?.FullName;
        }
        else if (!directoryManager.Exists(currentPath))
        {
            throw new InvalidProjectPathException($"The path '{currentPath}' is not a valid project path.");
        }
        
        while (currentPath != null)
        {
            if (directoryManager.GetDirectories(currentPath, ".git").Any())
            {
                var sourceControlRootDirectory = directoryManager.GetDirectoryInfo(currentPath).FullName;
                return sourceControlRootDirectory;
            }

            currentPath = directoryManager.GetDirectoryInfo(currentPath).Parent?.FullName;
        }

        return string.Empty;
    }

    public TagCollection GetGitTags(string currentPath)
    {
        var gitRoot = FindGitRootDirectory(currentPath);
        using var gitRepository = new Repository(gitRoot);
        return gitRepository.Tags;
    }

    public void StageChanges(string gitRoot, string currentPath)
    {
        using var gitRepository = new Repository(gitRoot);
        
        LibGit2Sharp.Commands.Stage(gitRepository, currentPath);
    }

    public void CommitChanges(string gitRoot, string commitMessage)
    {
        using var gitRepository = new Repository(gitRoot);

        var versionTime = DateTimeOffset.Now;
        var signature = gitRepository.Config.BuildSignature(versionTime);
        gitRepository.Commit(commitMessage, signature, signature);
    }

    public void AddTag(string gitRoot, string tagName)
    {
        using var gitRepository = new Repository(gitRoot);
        gitRepository.ApplyTag(tagName);
    }
    
    public List<string> GetTags(string gitRoot)
    {
        using var gitRepository = new Repository(gitRoot);
        return gitRepository.Tags.Select(x => x.FriendlyName).ToList();
    }
}