using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler,
    IGitHandler gitHandler,
    IConfigurationManager configurationManager,
    IChangelogHandler changelogHandler)
{
    public async Task ExecuteAsync(string? optionProjectPath, string? optionIncrementType, bool optionSkipVersionTagCheck)
    {
        if (string.IsNullOrEmpty(optionProjectPath))
            optionProjectPath = Directory.GetCurrentDirectory();
        var gitRoot = gitHandler.FindGitRootDirectory(optionProjectPath);
        var userConfiguration = await configurationManager.LoadUserConfiguration(gitRoot);
        var persistUserConfiguration = false;
        
        if (!Enum.TryParse(optionIncrementType, out IncrementType incrementType))
        {
            incrementType = IncrementType.Patch;
        }
        
        var availableProjects = await projectHandler.GetAvailableProjects(optionProjectPath);

        if (userConfiguration?.Projects?.Any() ?? false)
        {
            foreach (var project in userConfiguration.Projects)
            {
                project.ProjectDefinition = availableProjects.FirstOrDefault(x => x.ProjectPath.Equals(Path.Combine(optionProjectPath, project.Path.Replace('\\', Path.DirectorySeparatorChar))));
                if (project.ProjectDefinition is null)
                    throw new ConfiguredProjectNotFoundException($"The configured project '{project.Path}' does not exist in the specified path '{optionProjectPath}'.");
            }

            persistUserConfiguration = true;
        }
        else
        {
            if (userConfiguration is null)
                userConfiguration = new();

            if (userConfiguration.Projects is null)
                userConfiguration.Projects = [];
            
            foreach (var project in availableProjects)
            {
                userConfiguration.Projects.Add(new UserConfiguration.Project
                {
                    Path = project.ProjectPath,
                    ProjectDefinition = project,
                    IncrementType = incrementType
                });
            }
        }

        userConfiguration.GitRoot = gitRoot;

        if (!optionSkipVersionTagCheck)
        {
            foreach (var availableProject in userConfiguration.Projects)
            {
                if (availableProject.ProjectDefinition is null)
                    throw new InvalidUserConfigurationException($"The configured project '{availableProject.Path}' is invalid.");
                
                if (!projectHandler.ProjectHasVersionTag(availableProject.ProjectDefinition))
                    throw new NoVersionTagException($"The project '{availableProject.Path}' does not have a {ProjectConstants.VersionTag} tag. Add a {ProjectConstants.VersionTag} tag and run the tool again.");
            }
        }
        
        foreach (var availableProject in userConfiguration.Projects)
        {
            if (availableProject.ProjectDefinition is null)
                throw new InvalidUserConfigurationException($"The configured project '{availableProject.Path}' is invalid.");
        
            projectHandler.UpdateVersion(availableProject.ProjectDefinition, availableProject.IncrementType);
            gitHandler.StageChanges(gitRoot, availableProject.Path);
        }
        
        var dateTimeNow = DateTime.UtcNow;
        var nextVersionNumber = $"version_{dateTimeNow:yyyy-MM-dd.HH.mm.ss}";

        gitHandler.CommitChanges(gitRoot, $"chore: Release {dateTimeNow:yyyy-MM-dd}");
        
        gitHandler.AddTag(gitRoot, nextVersionNumber);
        
        var changelog = changelogHandler.GenerateChangelogAsMarkdown(userConfiguration, nextVersionNumber);
        await changelogHandler.PersistChangelog(userConfiguration, changelog, null);
        // When done, reset the config file if the user had one
        if (persistUserConfiguration)
        {
            await configurationManager.ResetUserConfiguration(gitRoot, userConfiguration);
        }
    }
}