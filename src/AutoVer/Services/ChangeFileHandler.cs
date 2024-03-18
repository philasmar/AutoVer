using System.Text.Json;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ChangeFileHandler(
    IPathManager pathManager,
    IDirectoryManager directoryManager,
    IFileManager fileManager,
    IToolInteractiveService toolInteractiveService,
    IGitHandler gitHandler) : IChangeFileHandler
{
    public ChangeFile GenerateChangeFile(UserConfiguration configuration, string? changeMessage)
    {
        var changeFile = new ChangeFile();
        foreach (var project in configuration.Projects)
        {
            changeFile.Projects.Add(new ProjectChange
            {
                Name = project.Name,
                ChangelogMessages = !string.IsNullOrEmpty(changeMessage) ? [changeMessage] : []
            });
        }

        return changeFile;
    }

    public async Task PersistChangeFile(UserConfiguration configuration, ChangeFile changeFile)
    {
        if (string.IsNullOrEmpty(configuration.GitRoot))
            throw new InvalidProjectException("The project path you have specified is not a valid git repository.");

        var changeFolder = pathManager.Combine(configuration.GitRoot, ConfigurationConstants.ConfigFolderName,
            ConfigurationConstants.ChangesFolderName);

        if (!directoryManager.Exists(changeFolder))
            directoryManager.CreateDirectory(changeFolder);

        var changeFilePath = pathManager.Combine(changeFolder, $"{Guid.NewGuid().ToString().ToLower()}.json");
        
        await fileManager.WriteAllTextAsync(changeFilePath, 
            JsonSerializer.Serialize(changeFile, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    public async Task<IList<ChangeFile>> LoadChangeFilesFromRepository(string repositoryRoot)
    {
        var changeFilesPath = pathManager.Combine(repositoryRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ChangesFolderName);

        if (!directoryManager.Exists(changeFilesPath))
            directoryManager.CreateDirectory(changeFilesPath);
        var changeFilePaths = directoryManager.GetFiles(changeFilesPath, "*.json").ToList();

        var changeFiles = new List<ChangeFile>();
        
        foreach (var changeFilePath in changeFilePaths)
        {
            try
            {
                var content = await fileManager.ReadAllTextAsync(changeFilePath);
                var changeFile = JsonSerializer.Deserialize<ChangeFile>(content);
                if (changeFile != null)
                    changeFiles.Add(changeFile);
            }
            catch (Exception)
            {
                toolInteractiveService.WriteErrorLine($"Unable to deserialize the change file '{changeFilePath}'.");
            }
        }

        return changeFiles;
    }

    public void ResetChangeFiles(UserConfiguration userConfiguration)
    {
        if (string.IsNullOrEmpty(userConfiguration.GitRoot))
            throw new InvalidProjectException("The project path you have specified is not a valid git repository.");

        var changeFolderPath = pathManager.Combine(userConfiguration.GitRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ChangesFolderName);
        if (!directoryManager.Exists(changeFolderPath))
            return;
        
        var changeFilePaths = directoryManager.GetFiles(changeFolderPath, "*", SearchOption.AllDirectories).ToList();

        foreach (var changeFilePath in changeFilePaths)
        {
            try
            {
                fileManager.Delete(changeFilePath);
            }
            catch (Exception)
            {
                toolInteractiveService.WriteErrorLine($"Unable to delete the change file '{changeFilePath}'.");
            }
        }
            
        gitHandler.StageChanges(userConfiguration, changeFolderPath);
    }
}