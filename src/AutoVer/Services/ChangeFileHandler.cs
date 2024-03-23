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
    public ChangeFile GenerateChangeFile(UserConfiguration configuration, IncrementType incrementType, string? projectName, string? changeMessage)
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
                Type = configuration.ChangeFilesDetermineIncrementType ? incrementType : null,
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
                Type = configuration.ChangeFilesDetermineIncrementType ? incrementType : null,
                ChangelogMessages = !string.IsNullOrEmpty(changeMessage) ? [changeMessage] : []
            });
        }

        return changeFile;
    }

    public async Task PersistChangeFile(UserConfiguration configuration, ChangeFile changeFile)
    {
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

    public async Task<IList<ChangeFile>> LoadChangeFilesFromRepository(string repositoryRoot, string? tagName = null)
    {
        var changeFilesPath = pathManager.Combine(ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ChangesFolderName);
        var absoluteChangeFilesPath = pathManager.Combine(repositoryRoot, changeFilesPath);

        if (!directoryManager.Exists(absoluteChangeFilesPath))
            directoryManager.CreateDirectory(absoluteChangeFilesPath);

        var changeFiles = new List<ChangeFile>();
        
        if (!string.IsNullOrEmpty(tagName))
        {
            var gitFiles = gitHandler.GetFolderByTag(repositoryRoot, tagName, changeFilesPath);
        
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
        }
        else
        {
            var changeFilePaths = directoryManager.GetFiles(changeFilesPath, "*.json").ToList();

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
        }
        
        return changeFiles;
    }

    public IDictionary<string, IncrementType> GetProjectIncrementTypesFromChangeFiles(IList<ChangeFile> changeFiles)
    {
        var projectIncrements = new Dictionary<string, IncrementType>();
        
        foreach (var changeFile in changeFiles)
        {
            foreach (var project in changeFile.Projects)
            {
                if (project.Type is null)
                    throw new InvalidChangeFileException(
                        $"The repository is configured to use change files to determine project increment type. The change file '{changeFile.Path}' does not specify an increment type.");

                var projectType = project.Type ?? IncrementType.Patch;
                
                if (projectIncrements.ContainsKey(project.Name))
                {
                    if (project.Type > projectIncrements[project.Name])
                        projectIncrements[project.Name] = projectType;
                }
                else
                {
                    projectIncrements[project.Name] = projectType;
                }
            }
        }

        return projectIncrements;
    }

    public void ResetChangeFiles(UserConfiguration userConfiguration)
    {
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