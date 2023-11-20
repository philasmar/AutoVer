using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler)
{
    public async Task ExecuteAsync(string? optionProjectPath, string? optionIncrementType, bool optionSkipVersionTagCheck)
    {
        if (!Enum.TryParse(optionIncrementType, out Increment incrementType))
        {
            incrementType = Increment.Patch;
        }
        var availableProjects = await projectHandler.GetAvailableProjects(optionProjectPath);

        if (!optionSkipVersionTagCheck)
        {
            foreach (var availableProject in availableProjects)
            {
                if (!projectHandler.ProjectHasVersionTag(availableProject))
                    throw new NoVersionTagException($"The project '{availableProject.ProjectPath}' does not have a {ProjectConstants.VersionTag} tag. Add a {ProjectConstants.VersionTag} tag and run the tool again.");
            }
        }
        
        foreach (var availableProject in availableProjects)
        {
            projectHandler.UpdateVersion(availableProject, incrementType);
        }
    }
}