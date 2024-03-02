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
    public async Task ExecuteAsync(
        string? optionProjectPath, 
        string? optionIncrementType, 
        bool optionSkipVersionTagCheck, 
        bool optionNoCommit, 
        bool optionNoTag)
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
        
        foreach (var availableProject in userConfiguration.Projects)
        {
            if (availableProject.ProjectDefinition is null)
                throw new InvalidUserConfigurationException($"The configured project '{availableProject.Path}' is invalid.");
        
            projectHandler.UpdateVersion(availableProject.ProjectDefinition, availableProject.IncrementType, availableProject.PrereleaseLabel);
            gitHandler.StageChanges(userConfiguration, availableProject.Path);
        }
        
        var dateTimeNow = DateTime.UtcNow;
        var nextVersionNumber = $"version_{dateTimeNow:yyyy-MM-dd.HH.mm.ss}";

        // When done, reset the config file if the user had one
        if (userConfiguration.PersistConfiguration)
        {
            await configurationManager.ResetUserConfiguration(userConfiguration, new UserConfigurationResetRequest
            {
                IncrementType = true
            });
        }

        if (!optionNoCommit)
        {
            gitHandler.CommitChanges(userConfiguration, $"Release {dateTimeNow:yyyy-MM-dd}");

            if (!optionNoTag)
            {
                gitHandler.AddTag(userConfiguration, nextVersionNumber);
            }
        }
    }
}