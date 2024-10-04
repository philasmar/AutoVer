using AutoVer.Models;

namespace AutoVer.Services;

public interface IVersionIncrementer
{
    ThreePartVersion GetCurrentVersion(string? versionText);
    ThreePartVersion GetNextVersion(string? versionText, IncrementType incrementType, string? prereleaseLabel = null);
    ThreePartVersion GetNextMaxVersion(List<UserConfiguration.Project> projects, IDictionary<string, IncrementType>? projectIncrements, IncrementType globalIncrementType);
}