using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ConfigurationManager(
    IFileManager fileManager,
    IGitHandler gitHandler,
    IPathManager pathManager,
    IToolInteractiveService toolInteractiveService) : IConfigurationManager
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
            using var stream = new MemoryStream(content);
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
}