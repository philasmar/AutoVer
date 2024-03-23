using System.Globalization;
using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.Services;

public class VersionHandler(
    IGitHandler gitHandler) : IVersionHandler
{
    private readonly DateTime _newVersion = DateTime.UtcNow;
    private readonly Dictionary<string, List<DateTime>> _versionNumbersCache = new();

    public string GetNewVersionTag()
    {
        return $"version_{_newVersion:yyyy-MM-dd.HH.mm.ss}";
    }

    public string GetNewReleaseName()
    {
        return $"Release {_newVersion:yyyy-MM-dd}";
    }

    public string GetCurrentVersionTag(string projectPath)
    {
        var gitRoot = gitHandler.FindGitRootDirectory(projectPath);
        var versionNumbers = GetVersionNumbers(gitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTag($"The Git repository '{gitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        
        var currentVersionDate = versionNumbers[0];
        return $"version_{currentVersionDate:yyyy-MM-dd.HH.mm.ss}";
    }

    public string GetCurrentVersionTag(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTag($"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        
        var currentVersionDate = versionNumbers[0];
        return $"version_{currentVersionDate:yyyy-MM-dd.HH.mm.ss}";
    }

    public string GetCurrentReleaseName(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTag($"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        
        var currentVersionDate = versionNumbers[0];
        return $"Release {currentVersionDate:yyyy-MM-dd}";
    }

    public string? GetLastVersionTag(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTag($"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");

        if (versionNumbers.Count > 1)
        {
            var lastVersionDate = versionNumbers[1];
            return $"version_{lastVersionDate:yyyy-MM-dd.HH.mm.ss}";
        }
        
        return null;
    }

    private List<DateTime> GetVersionNumbers(string gitRoot)
    {
        if (!_versionNumbersCache.TryGetValue(gitRoot, out var versionNumbers))
        {
            var tags = gitHandler.GetTags(gitRoot);
            versionNumbers = tags
                .Where(x => x.StartsWith("version_"))
                .Select(x => x.Replace("version_", ""))
                .Select(x => DateTime.ParseExact(x, "yyyy-MM-dd.HH.mm.ss", CultureInfo.InvariantCulture))
                .OrderDescending()
                .ToList();
            _versionNumbersCache[gitRoot] = versionNumbers;
        }

        return versionNumbers;
    }
}