using AutoVer.Models;

namespace AutoVer.Services;

public class ThreePartVersionIncrementer : IVersionIncrementer
{
    public ThreePartVersion GetCurrentVersion(string? versionText)
    {
        if (string.IsNullOrEmpty(versionText)) return new ThreePartVersion { Major = 0, Minor = 0, Patch = 1 };
        
        return ThreePartVersion.Parse(versionText);
    }

    public ThreePartVersion GetNextVersion(string? versionText, IncrementType incrementType = IncrementType.Patch, string? prereleaseLabel = null)
    {
        var currentVersion = GetCurrentVersion(versionText);
        
        var nextVersion = new ThreePartVersion
        {
            Major = currentVersion.Major,
            Minor = currentVersion.Minor,
            Patch = currentVersion.Patch,
            PrereleaseLabel = prereleaseLabel
        };

        switch (incrementType)
        {
            case IncrementType.Major:
                nextVersion.Major += 1;
                nextVersion.Minor = 0;
                nextVersion.Patch = 0;
                break;
            case IncrementType.Minor:
                nextVersion.Minor += 1;
                nextVersion.Patch = 0;
                break;
            case IncrementType.Patch:
                nextVersion.Patch += 1;
                break;
            case IncrementType.None:
                break;
        }

        return nextVersion;
    }
}