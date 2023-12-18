using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.Services;

public interface IGitHandler
{
    string FindGitRootDirectory(string? currentPath);
    TagCollection GetGitTags(string currentPath);
    void StageChanges(string gitRoot, string currentPath);
    void CommitChanges(string gitRoot, string commitMessage);
    void AddTag(string gitRoot, string tagName);
    List<string> GetTags(string? gitRoot);
    List<ConventionalCommit> GetVersionCommits(string? gitRoot, string? lastVersionTag = null);
}