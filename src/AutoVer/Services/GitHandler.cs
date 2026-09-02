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
            // A standard repository has `.git` as a directory. A linked git worktree
            // has `.git` as a file containing a `gitdir:` pointer to the actual git
            // directory. Treat both as the source-control root so we don't walk past
            // the worktree and find the main repository's `.git` directory instead.
            if (directoryManager.GetDirectories(currentPath, ".git").Any() ||
                fileManager.Exists(Path.Combine(currentPath, ".git")))
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

    public bool IsValidTagName(string tagName) =>
        !string.IsNullOrEmpty(tagName) && Reference.IsValidName($"refs/tags/{tagName}");

    public List<string> FindNearestReachableTags(string gitRoot, IReadOnlyCollection<string> candidateTagNames)
    {
        if (candidateTagNames.Count == 0)
            return [];

        using var gitRepository = new Repository(gitRoot);

        var head = gitRepository.Head.Tip;
        if (head is null)
            return [];

        var candidates = candidateTagNames.ToHashSet(StringComparer.Ordinal);
        var tagsByCommit = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var tag in gitRepository.Tags)
        {
            if (!candidates.Contains(tag.FriendlyName))
                continue;

            // PeeledTarget resolves an annotated tag through its annotation object to the commit
            // itself; Target would be the annotation and would never match a commit sha.
            var commitSha = (tag.PeeledTarget ?? tag.Target)?.Sha;
            if (commitSha is null)
                continue;

            if (!tagsByCommit.TryGetValue(commitSha, out var names))
                tagsByCommit[commitSha] = names = [];

            names.Add(tag.FriendlyName);
        }

        if (tagsByCommit.Count == 0)
            return [];

        // Walked newest-first from HEAD and stopped at the first tagged commit, so the result is the
        // nearest release rather than the highest-sorting one - and the walk costs only the distance
        // to that release, not the whole history.
        var filter = new CommitFilter
        {
            IncludeReachableFrom = head,
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
        };

        foreach (var commit in gitRepository.Commits.QueryBy(filter))
        {
            if (tagsByCommit.TryGetValue(commit.Sha, out var names))
                return names;
        }

        return [];
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

    // A tag can predate the path it's asked to look up - e.g. a project adopts
    // autover.json (or its first change file) only after already having release
    // tags. That must resolve like "no file at this tag" (null), not throw.
    public string? GetFileByTag(string gitRoot, string tagName, string filePath)
    {
        using var gitRepository = new Repository(gitRoot);
        var tag = gitRepository.Tags.First(x => x.FriendlyName.Equals(tagName));
        var commit = gitRepository.Lookup<Commit>(tag.Target.Sha);
        string[] paths = filePath.Split(pathManager.DirectorySeparatorChar);
        string fullPath = paths[0];
        Tree tree = commit.Tree;
        TreeEntry? entry = tree.FirstOrDefault(x => x.Path == fullPath);
        if (entry is null)
            return null;
        if(entry.TargetType == TreeEntryTargetType.Tree)
        {
            foreach(string pathPart in paths.Skip(1).ToArray())
            {
                if(entry.TargetType == TreeEntryTargetType.Tree)
                    tree = (Tree)entry.Target;

                fullPath += "/" + pathPart;
                entry = tree.FirstOrDefault(x => x.Path == fullPath);
                if (entry is null)
                    return null;
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
        var files = new List<GitFile>();
        TreeEntry? entry = tree.FirstOrDefault(x => x.Path == fullPath);
        if (entry is null)
            return files;
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