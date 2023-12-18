using AutoVer.Models;
using LibGit2Sharp;

namespace AutoVer.Services;

public interface ICommitHandler
{
    ConventionalCommit? Parse(Commit commit);
}