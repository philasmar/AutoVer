using AutoVer.Models;

namespace AutoVer.Services;

public interface IChangelogHandler
{
    ChangelogEntry GenerateChangelog(UserConfiguration configuration);
    Task PersistChangelog(UserConfiguration configuration, string changelog, string? path);
}