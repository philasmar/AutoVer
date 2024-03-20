using System.Text;
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
    public ChangeFile GenerateChangeFile(UserConfiguration configuration, string? projectName, string? changeMessage)
    {
        var changeFile = new ChangeFile();
        if (string.IsNullOrEmpty(projectName))
        {
            if (configuration.Projects.Count > 1)
            {
                throw new InvalidProjectNameSpecifiedException(
                    "You need to specify a project name with the change message. Use the '--project-name' argument to specify the project name.");
            }

            var project = configuration.Projects.First();
            changeFile.Projects.Add(new ProjectChange
            {
                Name = project.Name,
                ChangelogMessages = !string.IsNullOrEmpty(changeMessage) ? [changeMessage] : []
            });
        }
        else
        {
            var project = configuration.Projects.FirstOrDefault(x => x.Name.Equals(projectName));
            if (project is null)
                throw new InvalidProjectNameSpecifiedException(
                    $"The project '{projectName}' does not exist. Please specify a valid project name.");
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

    public async Task<IList<ChangeFile>> LoadChangeFilesFromRepository(string repositoryRoot, string tagName)
    {
        var changeFilesPath = pathManager.Combine(ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ChangesFolderName);
        var absoluteChangeFilesPath = pathManager.Combine(repositoryRoot, changeFilesPath);

        if (!directoryManager.Exists(absoluteChangeFilesPath))
            directoryManager.CreateDirectory(absoluteChangeFilesPath);
        var gitFiles = gitHandler.GetFolderByTag(repositoryRoot, tagName, changeFilesPath);

        var changeFiles = new List<ChangeFile>();
        
        foreach (var gitFile in gitFiles)
        {
            try
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(gitFile.Content));
                var changeFile = await JsonSerializer.DeserializeAsync<ChangeFile>(stream);
                if (changeFile != null)
                    changeFiles.Add(changeFile);
            }
            catch (Exception)
            {
                toolInteractiveService.WriteErrorLine($"Unable to deserialize the change file '{gitFile.Path}'.");
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