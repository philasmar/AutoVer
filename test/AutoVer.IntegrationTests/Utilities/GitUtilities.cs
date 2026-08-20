using LibGit2Sharp;

namespace AutoVer.IntegrationTests.Utilities;

internal static class GitUtilities
{
    public static List<string> GetAllTags(string gitRepositoryPath)
    {
        using var repo = new Repository(gitRepositoryPath);
        return repo.Tags.Select(x => x.FriendlyName).ToList();
    }

    public static string GetLastCommitMessage(string gitRepositoryPath)
    {
        using var repo = new Repository(gitRepositoryPath);
        return repo.Head.Tip.MessageShort;
    }

    public static int GetCommitCount(string gitRepositoryPath)
    {
        using var repo = new Repository(gitRepositoryPath);
        return repo.Commits.Count();
    }

    // True when the index has changes relative to HEAD that haven't been committed yet
    // (mirrors GitHandler.HasStagedChanges), regardless of whether they're staged.
    public static bool HasUncommittedChanges(string gitRepositoryPath)
    {
        using var repo = new Repository(gitRepositoryPath);
        var status = repo.RetrieveStatus();
        return status.IsDirty;
    }

    // True specifically when there are staged (index vs. HEAD) changes waiting to be committed.
    public static bool HasStagedChanges(string gitRepositoryPath)
    {
        using var repo = new Repository(gitRepositoryPath);
        var headTree = repo.Head.Tip.Tree;
        var changes = repo.Diff.Compare<TreeChanges>(headTree, DiffTargets.Index);
        return changes.Count > 0;
    }

    public static void StageChanges(string gitRepositoryPath, string currentPath)
    {
        var relativePath = Path.IsPathFullyQualified(currentPath) ? Path.GetRelativePath(gitRepositoryPath, currentPath) : currentPath;
        using var gitRepository = new Repository(gitRepositoryPath);
        string fullPath = !currentPath.Equals("*") ? Path.Combine(gitRepository.Info.WorkingDirectory, relativePath) : "*";
        LibGit2Sharp.Commands.Stage(gitRepository, fullPath);
    }

    public static void CommitChanges(string gitRepositoryPath, string commitMessage)
    {
        using var gitRepository = new Repository(gitRepositoryPath);
        var signature = gitRepository.Config.BuildSignature(DateTimeOffset.Now);
        gitRepository.Commit(commitMessage, signature, signature);
    }
}
