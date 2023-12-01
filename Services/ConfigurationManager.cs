using System.Text.Json;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ConfigurationManager(
    IFileManager fileManager,
    IPathManager pathManager) : IConfigurationManager
{
    private readonly string _configFolderName = ".autover";
    private readonly string _configFileName = "autover.json";

    public async Task<UserConfiguration?> LoadUserConfiguration(string repositoryRoot)
    {
        var configPath = pathManager.Combine(repositoryRoot, _configFolderName, _configFileName);
        if (!fileManager.Exists(configPath))
            return null;

        try
        {
            var content = await fileManager.ReadAllBytesAsync(configPath);
            await using var stream = new MemoryStream(content);
            var userConfiguration = await JsonSerializer.DeserializeAsync<UserConfiguration>(stream);

            return userConfiguration;
        }
        catch (Exception ex)
        {
            throw new InvalidUserConfigurationException(
                $"There was an issue loading the user configuration at '{configPath}'.", 
                ex);
        }
    }

    public async Task ResetUserConfiguration(string repositoryRoot, UserConfiguration userConfiguration)
    {
        var configPath = pathManager.Combine(repositoryRoot, _configFolderName, _configFileName);
        if (!fileManager.Exists(configPath))
            return;

        try
        {
            foreach (var project in userConfiguration.Projects)
            {
                project.Changelog = [];
                project.IncrementType = IncrementType.Patch;
                project.ProjectDefinition = null;
            }
            
            await using var stream = new FileStream(configPath, FileMode.Create);
            await using var sw = new StreamWriter(stream);
            await JsonSerializer.SerializeAsync(
                sw.BaseStream, 
                userConfiguration, 
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
        }
        catch (Exception ex)
        {
            throw new ResetUserConfigurationFailedException(
                $"Unable to reset the configuration file '{configPath}'.",
                ex);
        }
    }
}