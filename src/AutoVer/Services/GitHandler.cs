using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;
using LibGit2Sharp;

namespace AutoVer.Services;

public class GitHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager,
    IPathManager pathManager,
    ICommitHandler commitHandler) : IGitHandler
{
    private readonly Dictionary<string, string> _gitRootCache = new();
    
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

        if (_gitRootCache.TryGetValue(currentPath!, out var gitRoot))
            return gitRoot;
        
        while (currentPath != null)
        {
            if (directoryManager.GetDirectories(currentPath, ".git").Any())
            {
                var sourceControlRootDirectory = directoryManager.GetDirectoryInfo(currentPath).FullName;
                _gitRootCache[currentPath] = sourceControlRootDirectory;
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
        var relativePath = Path.IsPathFullyQualified(currentPath) ? Path.GetRelativePath(userConfiguration.GitRoot, currentPath) : currentPath;
        using (var gitRepository = new Repository(userConfiguration.GitRoot))
        {
            string fullPath = !currentPath.Equals("*") ? Path.Combine(gitRepository.Info.WorkingDirectory, relativePath) : "*";
            LibGit2Sharp.Commands.Stage(gitRepository, fullPath);
        }
    }

    public bool HasStagedChanges(UserConfiguration userConfiguration)
    {
        using (var gitRepository = new Repository(userConfiguration.GitRoot))
        {
            var headTree = gitRepository.Head.Tip.Tree;
            var changes = gitRepository.Diff.Compare<TreeChanges>(headTree, DiffTargets.Index);

            return changes.Count > 0;
        }
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
    
    public List<string> GetTags(string gitRoot)
    {
        using var gitRepository = new Repository(gitRoot);
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

    public string GetFileByTag(string gitRoot, string tagName, string filePath)
    {
        using var gitRepository = new Repository(gitRoot);
        var tag = gitRepository.Tags.First(x => x.FriendlyName.Equals(tagName));
        var commit = gitRepository.Lookup<Commit>(tag.Target.Sha);
        string[] paths = filePath.Split(pathManager.DirectorySeparatorChar);
        string fullPath = paths[0];
        Tree tree = commit.Tree;
        TreeEntry entry = tree.First(x => x.Path == fullPath);
        if(entry.TargetType == TreeEntryTargetType.Tree)
        {
            foreach(string pathPart in paths.Skip(1).ToArray())
            {
                if(entry.TargetType == TreeEntryTargetType.Tree)
                    tree = (Tree)entry.Target;

                fullPath += "/" + pathPart;
                entry = tree.First(x => x.Path == fullPath);
            }
        }
        Blob blob = (Blob) entry.Target;
        return blob.GetContentText();
    }
    
    public List<GitFile> GetFolderByTag(string gitRoot, string tagName, string folderPath)
    {
        using var gitRepository = new Repository(gitRoot);
        var tag = gitRepository.Tags.First(x => x.FriendlyName.Equals(tagName));
        var commit = gitRepository.Lookup<Commit>(tag.Target.Sha);
        string[] paths = folderPath.Split(pathManager.DirectorySeparatorChar);
        string fullPath = paths[0];
        Tree tree = commit.Tree;
        TreeEntry entry = tree.First(x => x.Path == fullPath);
        var files = new List<GitFile>();
        if(entry.TargetType == TreeEntryTargetType.Tree)
        {
            foreach(string pathPart in paths.Skip(1).ToArray())
            {
                if(entry.TargetType == TreeEntryTargetType.Tree)
                    tree = (Tree)entry.Target;

                fullPath += "/" + pathPart;
                var currentEntry = tree.FirstOrDefault(x => x.Path == fullPath);
                if (currentEntry is null)
                    return files;
                entry = currentEntry;
            }
        }


        if (entry.TargetType == TreeEntryTargetType.Tree)
        {
            foreach (var target in (Tree) entry.Target)
            {
                if (target.TargetType == TreeEntryTargetType.Tree)
                    continue;
                
                Blob blob = (Blob) target.Target;
                var content = blob.GetContentText();
                files.Add(new GitFile(target.Path, content));
            }
        }
        
        return files;
    }
}