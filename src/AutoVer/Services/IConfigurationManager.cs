using AutoVer.Models;

namespace AutoVer.Services;

public interface IConfigurationManager
{
    Task<UserConfiguration> RetrieveUserConfiguration(string? projectPath, IncrementType incrementType, string? tagName = null);
    Task ResetUserConfiguration(UserConfiguration userConfiguration, UserConfigurationResetRequest resetRequest);

    /// <summary>
    /// Reads only the repository-level settings from autover.json, without loading or validating
    /// any project file. For the callers that need a setting (such as the tag format) before a
    /// full configuration can be built. Returns null when the repository has no config file.
    /// </summary>
    UserConfiguration? LoadRepositorySettings(string gitRoot);
}