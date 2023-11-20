using AutoVer.Models;

namespace AutoVer.Services;

public interface IVersionIncrementer
{
    ThreePartVersion GetCurrentVersion(string? versionText);
    ThreePartVersion GetNextVersion(string? versionText, Increment increment = Increment.Patch);
}