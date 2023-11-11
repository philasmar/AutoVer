using AutoVer.Models;

namespace AutoVer.Services;

public class ThreePartVersionIncrementer : IVersionIncrementer
{
    public ThreePartVersion GetCurrentVersion(string? versionText)
    {
        if (string.IsNullOrEmpty(versionText)) return new ThreePartVersion { Major = 0, Minor = 0, Patch = 1 };
        
        return ThreePartVersion.Parse(versionText);
    }

    public ThreePartVersion GetNextVersion(string? versionText, string? nextVersion)
    {
        var currentVersion = GetCurrentVersion(versionText);

        if (!string.IsNullOrEmpty(nextVersion))
            return ThreePartVersion.Parse(nextVersion);
        
        return new ThreePartVersion
        {
            Major = currentVersion.Major,
            Minor = currentVersion.Minor,
            Patch = currentVersion.Patch + 1
        };
    }
}