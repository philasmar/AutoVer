using AutoVer.Extensions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler,
    IGitHandler gitHandler,
    IConfigurationManager configurationManager,
    IChangeFileHandler changeFileHandler,
    IVersionHandler versionHandler,
    IVersionIncrementer versionIncrementer)
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
                availableProject.EnsureProjectHasVersionTag();
            }
        }

        IDictionary<string, IncrementType> projectIncrements = new Dictionary<string, IncrementType>();
        if (userConfiguration.ChangeFilesDetermineIncrementType)
        {
            var changeFiles = await changeFileHandler.LoadChangeFilesFromRepository(userConfiguration.GitRoot);
            projectIncrements = changeFileHandler.GetProjectIncrementTypesFromChangeFiles(changeFiles);
        }

        ThreePartVersion? maxNextVersion = null;
        if (userConfiguration.UseSameVersionForAllProjects)
        {
            maxNextVersion = versionIncrementer.GetNextMaxVersion(
                userConfiguration.Projects, 
                userConfiguration.ChangeFilesDetermineIncrementType ? projectIncrements : null,
                incrementType);
        }

        var projectsIncremented = false;
        foreach (var availableProject in userConfiguration.Projects)
        {
            if (!availableProject.IncrementType.Equals(IncrementType.None))
                projectsIncremented = true;
            if (userConfiguration.UseSameVersionForAllProjects)
            {
                var projectIncrementType = availableProject.IncrementType ?? IncrementType.Patch;
                if (userConfiguration.ChangeFilesDetermineIncrementType &&
                    projectIncrements.ContainsKey(availableProject.Name))
                    projectIncrementType = projectIncrements[availableProject.Name];
                var localMaxVersion = versionIncrementer.GetNextMaxVersion(
                    availableProject,
                    userConfiguration.ChangeFilesDetermineIncrementType ? projectIncrements : null,
                    incrementType);
                foreach (var project in availableProject.Projects)
                {
                    projectHandler.UpdateVersion(
                        project.ProjectDefinition, 
                        projectIncrementType, 
                        availableProject.PrereleaseLabel,
                        optionUseVersion ?? maxNextVersion?.ToString() ?? localMaxVersion?.ToString());
                }
            }
            else
            {
                if (userConfiguration.ChangeFilesDetermineIncrementType)
                {
                    var projectIncrementType = IncrementType.None;
                    if (projectIncrements.ContainsKey(availableProject.Name))
                        projectIncrementType = projectIncrements[availableProject.Name];
                    if (projectIncrementType.Equals(IncrementType.None))
                        continue;
                    var localMaxVersion = versionIncrementer.GetNextMaxVersion(
                        availableProject,
                        userConfiguration.ChangeFilesDetermineIncrementType ? projectIncrements : null,
                        incrementType);
                    foreach (var project in availableProject.Projects)
                    {
                        projectHandler.UpdateVersion(
                            project.ProjectDefinition, 
                            projectIncrementType, 
                            availableProject.PrereleaseLabel,
                            optionUseVersion ?? localMaxVersion?.ToString());
                    }
                }
                else
                {
                    var projectIncrementType = availableProject.IncrementType ?? IncrementType.Patch;
                    var localMaxVersion = versionIncrementer.GetNextMaxVersion(
                        availableProject,
                        userConfiguration.ChangeFilesDetermineIncrementType ? projectIncrements : null,
                        incrementType);
                    if (projectIncrementType.Equals(IncrementType.None))
                        continue;
                    foreach (var project in availableProject.Projects)
                    {
                        projectHandler.UpdateVersion(
                            project.ProjectDefinition, 
                            projectIncrementType, 
                            availableProject.PrereleaseLabel, 
                            optionUseVersion ?? localMaxVersion?.ToString());
                    }
                }
            }

            foreach (var project in availableProject.Projects)
            {
                gitHandler.StageChanges(userConfiguration, project.Path);
            }
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
            if (gitHandler.HasStagedChanges(userConfiguration))
                gitHandler.CommitChanges(userConfiguration, versionHandler.GetNewReleaseName(userConfiguration));

            if (!optionNoTag)
            {
                gitHandler.AddTag(userConfiguration, versionHandler.GetNewVersionTag(userConfiguration));
            }
        }
    }
}