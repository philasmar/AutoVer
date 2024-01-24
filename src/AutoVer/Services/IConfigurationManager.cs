using AutoVer.Models;

namespace AutoVer.Services;

public interface IConfigurationManager
{
    Task<UserConfiguration> RetrieveUserConfiguration(string? projectPath, IncrementType incrementType);
    Task ResetUserConfiguration(UserConfiguration userConfiguration, UserConfigurationResetRequest resetRequest);
}