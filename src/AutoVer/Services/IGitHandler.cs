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
    List<ConventionalCommit> GetVersionCommits(UserConfiguration userConfiguration, string? lastVersionTag = null);
    string GetFileByTag(string gitRoot, string tagName, string filePath);
    List<GitFile> GetFolderByTag(string gitRoot, string tagName, string folderPath);
    bool HasStagedChanges(UserConfiguration userConfiguration);
}