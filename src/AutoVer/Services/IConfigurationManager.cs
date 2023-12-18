using AutoVer.Models;

namespace AutoVer.Services;

public interface IConfigurationManager
{
    Task<UserConfiguration?> LoadUserConfiguration(string repositoryRoot);
    Task ResetUserConfiguration(string repositoryRoot, UserConfiguration userConfiguration);
}