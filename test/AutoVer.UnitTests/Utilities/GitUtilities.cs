using LibGit2Sharp;
using static AutoVer.Services.VersionHandler;

namespace AutoVer.UnitTests.Utilities;

internal static class GitUtilities
{
    public static string GetLastTag(string gitRepositoryPath)
    {
        using (var repo = new Repository(gitRepositoryPath))
        {
            return repo.Tags
                .Select(x => x.FriendlyName)
                .Where(x => x.StartsWith("release_"))
                .Select(x => new VersionTag(x))
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Count)
                .FirstOrDefault()?
                .ToTagName() ?? string.Empty;
        }
    }

    public static string GetLastCommitMessage(string gitRepositoryPath)
    {
        using (var repo = new Repository(gitRepositoryPath))
        {
            // Get the last commit from HEAD
            Commit lastCommit = repo.Head.Tip;

            return lastCommit.MessageShort;
        }
    }

    public static void StageChanges(string gitRepositoryPath, string currentPath)
    {
        var relativePath = Path.IsPathFullyQualified(currentPath) ? Path.GetRelativePath(gitRepositoryPath, currentPath) : currentPath;
        using (var gitRepository = new Repository(gitRepositoryPath))
        {
            string fullPath = !currentPath.Equals("*") ? Path.Combine(gitRepository.Info.WorkingDirectory, relativePath) : "*";
            LibGit2Sharp.Commands.Stage(gitRepository, fullPath);
        }
    }

    public static void CommitChanges(string gitRepositoryPath, string commitMessage)
    {
        using (var gitRepository = new Repository(gitRepositoryPath))
        {
            var versionTime = DateTimeOffset.Now;
            var signature = gitRepository.Config.BuildSignature(versionTime);
            gitRepository.Commit(commitMessage, signature, signature);
        }
    }
}
