using AutoVer.Models;

namespace AutoVer.Services;

public interface IConfigurationManager
{
    Task<UserConfiguration?> LoadUserConfiguration(string repositoryRoot);
}