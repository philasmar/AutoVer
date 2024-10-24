using System.Text.Json;
using System.Text.Json.Serialization;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.Converters;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ConfigurationManager(
    IFileManager fileManager,
    IPathManager pathManager,
    IGitHandler gitHandler,
    IProjectHandler projectHandler,
    IChangeFileHandler changeFileHandler) : IConfigurationManager
{
    private async Task<UserConfiguration?> LoadUserConfigurationFromRepository(string repositoryRoot, string? tagName = null)
    {
        var configPath = string.Empty;
        
        try
        {
            if (string.IsNullOrEmpty(tagName))
            {
                configPath = pathManager.Combine(repositoryRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
                if (!fileManager.Exists(configPath))
                    return null;
            
                var content = await fileManager.ReadAllBytesAsync(configPath);
                await using var stream = new MemoryStream(content);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new UserConfigurationConverter(fileManager, pathManager));
                var userConfiguration = await JsonSerializer.DeserializeAsync<UserConfiguration>(stream, options);

                return userConfiguration;
            }
            else
            {
                configPath = pathManager.Combine(ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
                if (!fileManager.Exists(pathManager.Combine(repositoryRoot, configPath)))
                    return null;
                var fileContent = gitHandler.GetFileByTag(repositoryRoot, tagName, configPath);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new UserConfigurationConverter(fileManager, pathManager));
                var userConfiguration =  JsonSerializer.Deserialize<UserConfiguration>(fileContent, options);
                
                return userConfiguration;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidUserConfigurationException(
                $"There was an issue loading the user configuration at '{configPath}'.", 
                ex);
        }
    }

    public async Task<UserConfiguration> RetrieveUserConfiguration(string? projectPath, IncrementType incrementType, string? tagName = null)
    {
        if (string.IsNullOrEmpty(projectPath))
            projectPath = Directory.GetCurrentDirectory();
        var gitRoot = gitHandler.FindGitRootDirectory(projectPath);
        var userConfiguration = await LoadUserConfigurationFromRepository(gitRoot, tagName);
        
        var availableProjects = await projectHandler.GetAvailableProjects(projectPath);

        if (userConfiguration?.Projects?.Any() ?? false)
        {
            userConfiguration.GitRoot = gitRoot;
            userConfiguration.PersistConfiguration = true;
        }
        else
        {
            if (userConfiguration is null)
                userConfiguration = new()
                {
                    GitRoot = gitRoot
                };

            if (userConfiguration.Projects is null)
                userConfiguration.Projects = [];
            
            foreach (var project in availableProjects)
            {
                userConfiguration.Projects.Add(new ProjectContainer
                {
                    Name = GetProjectName(project.ProjectPath),
                    Path = project.ProjectPath,
                    Projects = [new(project.ProjectPath, project)],
                    IncrementType = incrementType
                });
            }
        }
        
        if (string.IsNullOrEmpty(userConfiguration.GitRoot))
            throw new InvalidProjectException("The project path you have specified is not a valid git repository.");

        return userConfiguration;
    }

    public async Task ResetUserConfiguration(UserConfiguration userConfiguration, UserConfigurationResetRequest resetRequest)
    {
        var configPath = pathManager.Combine(userConfiguration.GitRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
        if (!fileManager.Exists(configPath))
            return;

        try
        {
            foreach (var project in userConfiguration.Projects)
            {
                if (resetRequest.Changelog)
                    changeFileHandler.ResetChangeFiles(userConfiguration);
                
                if (resetRequest.IncrementType)
                    project.IncrementType = userConfiguration.DefaultIncrementType;
            }

            await using (var stream = new FileStream(configPath, FileMode.Create))
            {
                await using (var sw = new StreamWriter(stream))
                {
                    await JsonSerializer.SerializeAsync(
                        sw.BaseStream, 
                        userConfiguration, 
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        });
                }
            }
            
            gitHandler.StageChanges(userConfiguration, configPath);
        }
        catch (Exception ex)
        {
            throw new ResetUserConfigurationFailedException(
                $"Unable to reset the configuration file '{configPath}'.",
                ex);
        }
    }

    private string GetProjectName(string projectPath)
    {
        var projectParts = projectPath.Split(pathManager.DirectorySeparatorChar);
        if (projectParts.Length == 0)
            throw new InvalidProjectException($"The project '{projectPath}' is invalid.");
        var projectFileName = projectParts.Last();
        var projectFileNameParts = projectFileName.Split('.');
        if (projectFileNameParts.Length < 2)
            throw new InvalidProjectException($"The project '{projectPath}' is invalid.");
        return projectFileName.Replace($".{projectFileNameParts.Last()}", "");
    }
}