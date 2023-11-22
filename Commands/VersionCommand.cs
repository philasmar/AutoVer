using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler,
    IGitHandler gitHandler)
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
        
        var gitRoot = gitHandler.FindGitRootDirectory(optionProjectPath);
        
        foreach (var availableProject in availableProjects)
        {
            projectHandler.UpdateVersion(availableProject, incrementType);
            gitHandler.StageChanges(gitRoot, availableProject.ProjectPath);
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