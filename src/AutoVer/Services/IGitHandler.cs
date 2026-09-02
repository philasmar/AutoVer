using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.Services;

public interface IGitHandler
{
    string FindGitRootDirectory(string? currentPath);
    TagCollection GetGitTags(UserConfiguration userConfiguration, string currentPath);
    void StageChanges(UserConfiguration userConfiguration, string currentPath);
    void CommitChanges(UserConfiguration userConfiguration, string commitMessage);
    void AddTag(UserConfiguration userConfiguration, string tagName);
    List<string> GetTags(string gitRoot);

    /// <summary>
    /// Whether git would accept <paramref name="tagName"/> as a tag. Defers to git's own ref
    /// grammar rather than reimplementing it - the rules cover more than illegal characters
    /// (no '..', no trailing '.', no '.lock' suffix, no '@{', no empty path segment).
    /// </summary>
    bool IsValidTagName(string tagName);

    /// <summary>
    /// Of <paramref name="candidateTagNames"/>, the ones on the nearest commit reachable from HEAD
    /// that carries any of them - i.e. the release the current working state belongs to, the same
    /// notion as `git describe --tags --abbrev=0`. Empty when none is reachable (an unmerged branch,
    /// or a shallow clone whose history doesn't reach one).
    /// </summary>
    List<string> FindNearestReachableTags(string gitRoot, IReadOnlyCollection<string> candidateTagNames);
    List<ConventionalCommit> GetVersionCommits(UserConfiguration userConfiguration, string? lastVersionTag = null);
    string? GetFileByTag(string gitRoot, string tagName, string filePath);
    List<GitFile> GetFolderByTag(string gitRoot, string tagName, string folderPath);
    bool HasStagedChanges(UserConfiguration userConfiguration);
}