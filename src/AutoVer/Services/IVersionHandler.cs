using AutoVer.Models;

namespace AutoVer.Services;

public interface IVersionHandler
{
    string GetNewVersionTag();
    string GetNewReleaseName();
    string GetCurrentVersionTag(string projectPath);
    string GetCurrentVersionTag(UserConfiguration configuration);
    string GetCurrentReleaseName(UserConfiguration configuration);
    string? GetLastVersionTag(UserConfiguration configuration);
}