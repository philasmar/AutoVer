using System.Globalization;
using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.Services;

public class VersionHandler(
    IGitHandler gitHandler) : IVersionHandler
{
    private readonly DateTime _newVersion = DateTime.UtcNow;
    private readonly Dictionary<string, List<VersionTag>> _versionNumbersCache = new();

    public string GetNewVersionTag(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            return $"release_{_newVersion:yyyy-MM-dd}";
        else
        {
            var currentVersion = versionNumbers[0];
            if ($"{_newVersion:yyyy-MM-dd}".Equals($"{currentVersion.Date:yyyy-MM-dd}"))
                return $"release_{currentVersion.Date:yyyy-MM-dd}_{currentVersion.Count + 1}";
            else
                return $"release_{_newVersion:yyyy-MM-dd}";
        }
    }

    public string GetNewReleaseName(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            return $"Release {_newVersion:yyyy-MM-dd}";
        else
        {
            var currentVersion = versionNumbers[0];
            if ($"{_newVersion:yyyy-MM-dd}".Equals($"{currentVersion.Date:yyyy-MM-dd}"))
                return $"Release {currentVersion.Date:yyyy-MM-dd} #{currentVersion.Count + 1}";
            else
                return $"release_{_newVersion:yyyy-MM-dd}";
        }
    }

    public string GetCurrentVersionTag(string projectPath)
    {
        var gitRoot = gitHandler.FindGitRootDirectory(projectPath);
        var versionNumbers = GetVersionNumbers(gitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTagException($"The Git repository '{gitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        
        var currentVersionDate = versionNumbers[0];
        return currentVersionDate.ToTagName();
    }

    public string GetCurrentVersionTag(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTagException($"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        
        var currentVersionDate = versionNumbers[0];
        return currentVersionDate.ToTagName();
    }

    public string GetCurrentReleaseName(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTagException($"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        
        var currentVersionDate = versionNumbers[0];
        return currentVersionDate.ToReleaseName();
    }

    public string? GetLastVersionTag(UserConfiguration configuration)
    {
        var versionNumbers = GetVersionNumbers(configuration.GitRoot);
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTagException($"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");

        if (versionNumbers.Count > 1)
        {
            var lastVersionDate = versionNumbers[1];
            return lastVersionDate.ToTagName();
        }
        
        return null;
    }

    private List<VersionTag> GetVersionNumbers(string gitRoot)
    {
        if (!_versionNumbersCache.TryGetValue(gitRoot, out var versionNumbers))
        {
            var tags = gitHandler.GetTags(gitRoot);
            versionNumbers = tags
                .Where(x => x.StartsWith("release_"))
                .Select(x => new VersionTag(x))
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Count)
                .ToList();
            _versionNumbersCache[gitRoot] = versionNumbers;
        }

        return versionNumbers;
    }

    class VersionTag
    {
        public string Prefix { get; set; }
        public DateTime Date { get; set; }
        public int Count { get; set; }

        public VersionTag(string tag)
        {
            var parts = tag.Split("_");
            if (parts.Length < 2)
                throw new InvalidVersionTagException($"The tag '{tag}' is not supported.");
            Prefix = parts[0];
            Date = DateTime.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (parts.Length == 3)
                Count = int.Parse(parts[2]);
            else
                Count = 1;
        }

        public string ToTagName()
        {
            if (Count > 1)
            {
                return $"{Prefix}_{Date:yyyy-MM-dd}_{Count}";
            }
            
            return $"{Prefix}_{Date:yyyy-MM-dd}";
        }

        public string ToReleaseName()
        {
            if (Count > 1)
            {
                return $"Release {Date:yyyy-MM-dd} #{Count}";
            }
            
            return $"Release {Date:yyyy-MM-dd}";
        }
    }
}