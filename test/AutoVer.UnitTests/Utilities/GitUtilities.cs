using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.UnitTests.Utilities;

internal static class GitUtilities
{
    public static string GetLastTag(string gitRepositoryPath, string? tagFormat = null)
    {
        var format = VersionTagFormat.Parse(
            tagFormat ?? UserConfiguration.DefaultTagFormat,
            nameof(UserConfiguration.TagFormat));

        using (var repo = new Repository(gitRepositoryPath))
        {
            var versionTags = new List<VersionTag>();
            foreach (var friendlyName in repo.Tags.Select(tag => tag.FriendlyName))
            {
                if (format.TryParseTag(friendlyName, out var parsed))
                    versionTags.Add(parsed!);
            }

            versionTags.Sort((left, right) => right.CompareTo(left));

            return versionTags.Count > 0 ? versionTags[0].Raw : string.Empty;
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
