using AutoVer.Models;

namespace AutoVer.Services;

public class ThreePartVersionIncrementer : IVersionIncrementer
{
    public ThreePartVersion GetCurrentVersion(string? versionText)
    {
        if (string.IsNullOrEmpty(versionText)) return new ThreePartVersion { Major = 0, Minor = 0, Patch = 1 };
        
        return ThreePartVersion.Parse(versionText);
    }

    public ThreePartVersion GetNextVersion(string? versionText, Increment increment = Increment.Patch)
    {
        var currentVersion = GetCurrentVersion(versionText);
        
        var nextVersion = new ThreePartVersion
        {
            Major = currentVersion.Major,
            Minor = currentVersion.Minor,
            Patch = currentVersion.Patch
        };

        switch (increment)
        {
            case Increment.Major:
                nextVersion.Major += 1;
                nextVersion.Minor = 0;
                nextVersion.Patch = 0;
                break;
            case Increment.Minor:
                nextVersion.Minor += 1;
                nextVersion.Patch = 0;
                break;
            case Increment.Patch:
                nextVersion.Patch += 1;
                break;
        }

        return nextVersion;
    }
}