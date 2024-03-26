using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler,
    IGitHandler gitHandler,
    IConfigurationManager configurationManager,
    IChangeFileHandler changeFileHandler,
    IVersionHandler versionHandler)
{
    public async Task ExecuteAsync(
        string? optionProjectPath, 
        string? optionIncrementType, 
        bool optionSkipVersionTagCheck, 
        bool optionNoCommit, 
        bool optionNoTag,
        string? optionUseVersion)
    {
        if (!Enum.TryParse(optionIncrementType, out IncrementType incrementType))
        {
            incrementType = IncrementType.Patch;
        }
        
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(optionProjectPath, incrementType);
        
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

        IDictionary<string, IncrementType> projectIncrements = new Dictionary<string, IncrementType>();
        if (userConfiguration.ChangeFilesDetermineIncrementType)
        {
            var changeFiles = await changeFileHandler.LoadChangeFilesFromRepository(userConfiguration.GitRoot);
            projectIncrements = changeFileHandler.GetProjectIncrementTypesFromChangeFiles(changeFiles);
        }

        var projectsIncremented = false;
        foreach (var availableProject in userConfiguration.Projects)
        {
            if (availableProject.ProjectDefinition is null)
                throw new InvalidUserConfigurationException($"The configured project '{availableProject.Path}' is invalid.");
            if (!availableProject.IncrementType.Equals(IncrementType.None))
                projectsIncremented = true;
            if (userConfiguration.ChangeFilesDetermineIncrementType)
            {
                var projectIncrementType = IncrementType.None;
                if (projectIncrements.ContainsKey(availableProject.Name))
                    projectIncrementType = projectIncrements[availableProject.Name];
                projectHandler.UpdateVersion(availableProject.ProjectDefinition, projectIncrementType, availableProject.PrereleaseLabel, optionUseVersion);
            }
            else
            {
                var projectIncrementType = availableProject.IncrementType ?? IncrementType.Patch;
                projectHandler.UpdateVersion(availableProject.ProjectDefinition, projectIncrementType, availableProject.PrereleaseLabel, optionUseVersion);
            }
            gitHandler.StageChanges(userConfiguration, availableProject.Path);
        }

        // When done, reset the config file if the user had one
        if (userConfiguration.PersistConfiguration)
        {
            if (!userConfiguration.ChangeFilesDetermineIncrementType)
            {
                await configurationManager.ResetUserConfiguration(userConfiguration, new UserConfigurationResetRequest
                {
                    IncrementType = true
                });
            }
        }

        if (!projectsIncremented && string.IsNullOrEmpty(optionUseVersion))
            return;

        if (!optionNoCommit)
        {
            gitHandler.CommitChanges(userConfiguration, versionHandler.GetNewReleaseName(userConfiguration));

            if (!optionNoTag)
            {
                gitHandler.AddTag(userConfiguration, versionHandler.GetNewVersionTag(userConfiguration));
            }
        }
    }
}