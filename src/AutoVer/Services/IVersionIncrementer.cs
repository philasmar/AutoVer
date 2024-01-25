using AutoVer.Models;

namespace AutoVer.Services;

public interface IVersionIncrementer
{
    ThreePartVersion GetCurrentVersion(string? versionText);
    ThreePartVersion GetNextVersion(string? versionText, IncrementType incrementType = IncrementType.Patch, string? prereleaseLabel = null);
}