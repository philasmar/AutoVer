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
}