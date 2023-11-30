using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler,
    IGitHandler gitHandler,
    IConfigurationManager configurationManager)
{
    public async Task ExecuteAsync(string? optionProjectPath, string? optionIncrementType, bool optionSkipVersionTagCheck)
    {
        var gitRoot = gitHandler.FindGitRootDirectory(optionProjectPath);
        var userConfiguration = await configurationManager.LoadUserConfiguration(gitRoot);
        
        if (!Enum.TryParse(optionIncrementType, out IncrementType incrementType))
        {
            incrementType = IncrementType.Patch;
        }
        
        var availableProjects = await projectHandler.GetAvailableProjects(optionProjectPath);

        if (userConfiguration?.Projects?.Any() ?? false)
        {
            foreach (var project in userConfiguration.Projects)
            {
                project.ProjectDefinition = availableProjects.FirstOrDefault(x => x.ProjectPath.Equals(project.Path.Replace('\\', Path.DirectorySeparatorChar)));
                if (project.ProjectDefinition is null)
                    throw new ConfiguredProjectNotFoundException($"The configured project '{project.Path}' does not exist in the specified path '{optionProjectPath}'.");
            }
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
                    ProjectDefinition = project
                });
            }
        }

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

            projectHandler.UpdateVersion(availableProject.ProjectDefinition, incrementType);
            gitHandler.StageChanges(gitRoot, availableProject.Path);
        }

        var tags = gitHandler.GetTags(gitRoot);
        var versionNumbers = tags
            .Where(x => x.StartsWith("release_"))
            .Select(x => x.Replace("release_", ""))
            .Select(int.Parse)
            .ToList();
        var nextVersionNumber = versionNumbers.Any() ? versionNumbers.Max() + 1 : 1;
        
        gitHandler.CommitChanges(gitRoot, $"chore: Release {nextVersionNumber}");
        
        gitHandler.AddTag(gitRoot, $"release_{nextVersionNumber}");
    }
}