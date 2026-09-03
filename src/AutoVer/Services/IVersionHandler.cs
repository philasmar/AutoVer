using AutoVer.Models;

namespace AutoVer.Services;

public interface IVersionHandler
{
    string GetNewVersionTag(UserConfiguration configuration);
    string GetNewReleaseName(UserConfiguration configuration);
    string GetCurrentVersionTag(string projectPath);
    string GetCurrentVersionTag(UserConfiguration configuration);
    string GetCurrentReleaseName(UserConfiguration configuration);
    string? GetLastVersionTag(UserConfiguration configuration);

    /// <summary>
    /// The version carried by the current release tag, or null when the repository has no release
    /// yet. For a repository whose version comes from its tags, this is where the current version is
    /// read from instead of a project file.
    /// </summary>
    ThreePartVersion? GetCurrentTagVersion(UserConfiguration configuration);
}