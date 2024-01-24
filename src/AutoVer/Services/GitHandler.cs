using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;
using LibGit2Sharp;

namespace AutoVer.Services;

public class GitHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager,
    ICommitHandler commitHandler) : IGitHandler
{
    public string FindGitRootDirectory(string? currentPath)
    {
        if (string.IsNullOrEmpty(currentPath))
            throw new InvalidProjectException($"The provided project path is empty or invalid.");
        
        if (fileManager.Exists(currentPath))
        {
            currentPath = directoryManager.GetDirectoryInfo(currentPath).Parent?.FullName;
        }
        else if (!directoryManager.Exists(currentPath))
        {
            throw new InvalidProjectException($"The path '{currentPath}' is not a valid project path.");
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

    public TagCollection GetGitTags(UserConfiguration userConfiguration, string currentPath)
    {
        using var gitRepository = new Repository(userConfiguration.GitRoot);
        return gitRepository.Tags;
    }

    public void StageChanges(UserConfiguration userConfiguration, string currentPath)
    {
        using var gitRepository = new Repository(userConfiguration.GitRoot);
        
        LibGit2Sharp.Commands.Stage(gitRepository, currentPath);
    }

    public void CommitChanges(UserConfiguration userConfiguration, string commitMessage)
    {
        using var gitRepository = new Repository(userConfiguration.GitRoot);

        var versionTime = DateTimeOffset.Now;
        var signature = gitRepository.Config.BuildSignature(versionTime);
        gitRepository.Commit(commitMessage, signature, signature);
    }

    public void AddTag(UserConfiguration userConfiguration, string tagName)
    {
        using var gitRepository = new Repository(userConfiguration.GitRoot);
        gitRepository.ApplyTag(tagName);
    }
    
    public List<string> GetTags(UserConfiguration userConfiguration)
    {
        using var gitRepository = new Repository(userConfiguration.GitRoot);
        return gitRepository.Tags.Select(x => x.FriendlyName).ToList();
    }

    public List<ConventionalCommit> GetVersionCommits(UserConfiguration userConfiguration, string? lastVersionTag = null)
    {
        using var gitRepository = new Repository(userConfiguration.GitRoot);

        var lastTag = !string.IsNullOrEmpty(lastVersionTag) ? 
            gitRepository.Tags.First(x => x.FriendlyName.Equals(lastVersionTag)) :
            null;

        if (lastTag is not null)
        {
            var filter = new CommitFilter
            {
                ExcludeReachableFrom = lastTag
            };

            var commits = gitRepository.Commits.QueryBy(filter).ToList();
            return commits.Select(commitHandler.Parse).Where(x => x != null).ToList()!;
        }
        else
        {
            var commits = gitRepository.Commits.ToList();
            return commits.Select(commitHandler.Parse).Where(x => x != null).ToList()!;
        }
    }
}