using LibGit2Sharp;

namespace AutoVer.Services;

public interface IGitHandler
{
    string FindGitRootDirectory(string currentPath);
    TagCollection GetGitTags(string currentPath);
}