using AutoVer.Models;

namespace AutoVer.Services;

public interface IConfigurationManager
{
    Task<UserConfiguration> RetrieveUserConfiguration(string? projectPath, IncrementType incrementType, string? tagName = null);
    Task ResetUserConfiguration(UserConfiguration userConfiguration, UserConfigurationResetRequest resetRequest);
}