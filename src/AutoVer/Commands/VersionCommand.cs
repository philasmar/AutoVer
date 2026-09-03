using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class VersionCommand(
    IProjectHandler projectHandler,
    IGitHandler gitHandler,
    IConfigurationManager configurationManager,
    IChangeFileHandler changeFileHandler,
    IVersionHandler versionHandler,
    IVersionIncrementer versionIncrementer,
    IToolInteractiveService toolInteractiveService)
{
    public async Task ExecuteAsync(
        string? optionProjectPath,
        string? optionIncrementType,
        bool optionSkipVersionTagCheck,
        bool optionNoCommit,
        bool optionNoTag,
        string? optionUseVersion,
        bool optionCurrent)
    {
        var incrementType = IncrementTypeParser.Parse(optionIncrementType);

        var userConfiguration = await configurationManager.RetrieveUserConfiguration(optionProjectPath, incrementType);

        if (optionCurrent)
        {
            PrintCurrentVersions(userConfiguration);
            return;
        }

        IDictionary<string, IncrementType> projectIncrements = new Dictionary<string, IncrementType>();
        if (userConfiguration.ChangeFilesDetermineIncrementType)
        {
            var changeFiles = await changeFileHandler.LoadChangeFilesFromRepository(userConfiguration.GitRoot);
            projectIncrements = changeFileHandler.GetProjectIncrementTypesFromChangeFiles(changeFiles);
        }

        if (userConfiguration.VersionFromTag)
        {
            await ReleaseFromTagAsync(userConfiguration, projectIncrements, incrementType, optionUseVersion, optionNoCommit, optionNoTag);
            return;
        }

        // A project file that carries no version yet is seeded with the configured initial version,
        // taken as-is, and the element is created when it's written. Decided per container, since a
        // container is one version: a container with some versions present takes its version from
        // those, and only one with none needs seeding.
        var repositorySeedVersion = HasNoVersionAnywhere(userConfiguration.Projects)
            ? userConfiguration.EffectiveInitialVersion
            : null;

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
                        optionUseVersion ?? repositorySeedVersion ?? maxNextVersion?.ToString() ?? localMaxVersion?.ToString());
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
                            optionUseVersion ?? SeedVersionFor(availableProject, userConfiguration) ?? localMaxVersion?.ToString());
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
                            optionUseVersion ?? SeedVersionFor(availableProject, userConfiguration) ?? localMaxVersion?.ToString());
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
            // Resolved before committing, deliberately: a tag name that git would reject, or that
            // collides with an existing release, must fail while the repository is still untouched
            // rather than leaving behind a release commit with no tag on it.
            var tagName = optionNoTag ? null : versionHandler.GetNewVersionTag(userConfiguration);

            if (gitHandler.HasStagedChanges(userConfiguration))
                gitHandler.CommitChanges(userConfiguration, versionHandler.GetNewReleaseName(userConfiguration));

            if (tagName is not null)
            {
                gitHandler.AddTag(userConfiguration, tagName);
            }
        }
    }

    /// <summary>
    /// Whether nothing in the repository carries a version yet, in which case the whole repository is
    /// seeded from the configured initial version rather than incremented from nothing.
    /// </summary>
    private static bool HasNoVersionAnywhere(List<ProjectContainer> projects)
    {
        var allProjects = projects.SelectMany(container => container.Projects).ToList();

        return allProjects.Count > 0 &&
               allProjects.All(project => string.IsNullOrEmpty(project.ProjectDefinition.Version));
    }

    /// <summary>
    /// The version to seed a container with, or null when it already carries one. A container is one
    /// version, so a container with any version present takes its version from that rather than
    /// being seeded - only one with none needs it.
    /// </summary>
    private static string? SeedVersionFor(ProjectContainer container, UserConfiguration userConfiguration) =>
        HasNoVersionAnywhere([container]) ? userConfiguration.EffectiveInitialVersion : null;

    /// <summary>
    /// Cuts a release for a repository whose version lives in its tags. There is no file to rewrite,
    /// so nothing is staged and no version-bump commit is produced - the tag is applied to HEAD, and
    /// the release's only content is whatever `autover changelog` commits afterwards.
    /// </summary>
    private async Task ReleaseFromTagAsync(
        UserConfiguration userConfiguration,
        IDictionary<string, IncrementType> projectIncrements,
        IncrementType optionIncrementType,
        string? optionUseVersion,
        bool optionNoCommit,
        bool optionNoTag)
    {
        var currentVersion = versionHandler.GetCurrentTagVersion(userConfiguration);

        ThreePartVersion releaseVersion;
        if (!string.IsNullOrEmpty(optionUseVersion))
        {
            if (!ThreePartVersion.TryParse(optionUseVersion, out releaseVersion))
                throw new InvalidArgumentException($"The version '{optionUseVersion}' you are trying to update to is invalid.");
        }
        else
        {
            // Checked before the first-release branch below, so that "nothing was asked for" means
            // no release whether or not the repository has released before. Deciding it afterwards
            // let a first release be cut from an empty set of changes while every release after it
            // was correctly declined.
            var resolvedIncrementType = ResolveIncrementTypeFromTag(userConfiguration, projectIncrements, optionIncrementType);
            if (resolvedIncrementType.Equals(IncrementType.None))
                return;

            var prereleaseLabel = userConfiguration.Projects
                .Select(project => project.PrereleaseLabel)
                .FirstOrDefault(label => !string.IsNullOrEmpty(label));

            if (currentVersion is null)
            {
                // The first release takes the configured initial version as-is: there is no earlier
                // release to increment from, so incrementing would skip the version that was asked
                // for. The prerelease label still applies though - otherwise configuring one would
                // be ignored on the first release and honoured on every release after it.
                releaseVersion = ThreePartVersion.Parse(userConfiguration.EffectiveInitialVersion);
                releaseVersion.PrereleaseLabel ??= prereleaseLabel;
            }
            else
            {
                releaseVersion = versionIncrementer.GetNextVersion(
                    currentVersion.ToString(),
                    resolvedIncrementType,
                    prereleaseLabel);
            }
        }

        userConfiguration.ResolvedReleaseVersion = releaseVersion;

        if (userConfiguration.PersistConfiguration && !userConfiguration.ChangeFilesDetermineIncrementType)
        {
            await configurationManager.ResetUserConfiguration(userConfiguration, new UserConfigurationResetRequest
            {
                IncrementType = true
            });
        }

        if (optionNoCommit)
            return;

        // Only the configuration reset above can have staged anything; the release itself writes no file.
        if (gitHandler.HasStagedChanges(userConfiguration))
            gitHandler.CommitChanges(userConfiguration, versionHandler.GetNewReleaseName(userConfiguration));

        if (!optionNoTag)
            gitHandler.AddTag(userConfiguration, versionHandler.GetNewVersionTag(userConfiguration));
    }

    /// <summary>
    /// One tag carries one version for the whole repository, so the largest increment any project
    /// asked for wins - a Major change anywhere makes the release a Major one.
    /// </summary>
    private static IncrementType ResolveIncrementTypeFromTag(
        UserConfiguration userConfiguration,
        IDictionary<string, IncrementType> projectIncrements,
        IncrementType optionIncrementType)
    {
        if (!userConfiguration.ChangeFilesDetermineIncrementType)
        {
            if (!optionIncrementType.Equals(IncrementType.None))
                return optionIncrementType;

            return userConfiguration.Projects
                .Select(project => project.IncrementType)
                .FirstOrDefault(increment => increment is not null) ?? userConfiguration.DefaultIncrementType;
        }

        var requested = userConfiguration.Projects
            .Where(project => projectIncrements.ContainsKey(project.Name))
            .Select(project => projectIncrements[project.Name])
            .ToList();

        if (requested.Contains(IncrementType.Major)) return IncrementType.Major;
        if (requested.Contains(IncrementType.Minor)) return IncrementType.Minor;
        if (requested.Contains(IncrementType.Patch)) return IncrementType.Patch;

        return IncrementType.None;
    }

    // A single project (the common case) prints as a bare value, matching
    // `changelog --release-name`'s convention for shell capture, e.g.
    // VERSION=$(autover version --current). Multiple projects print
    // labeled, since there's no single "the" version to capture bare.
    private void PrintCurrentVersions(UserConfiguration userConfiguration)
    {
        if (userConfiguration.VersionFromTag)
        {
            var tagVersion = versionHandler.GetCurrentTagVersion(userConfiguration);
            if (tagVersion is null)
                throw new InvalidVersionTagException(
                    $"The Git repository '{userConfiguration.GitRoot}' has no release tag to read a version from yet. " +
                    "Please run 'autover version' first.");

            toolInteractiveService.WriteLine(tagVersion.ToString());
            return;
        }

        var allProjects = userConfiguration.Projects
            .SelectMany(container => container.Projects)
            .ToList();

        if (allProjects.Count == 1)
        {
            toolInteractiveService.WriteLine(allProjects[0].ProjectDefinition.Version);
            return;
        }

        foreach (var container in userConfiguration.Projects)
        {
            foreach (var project in container.Projects)
            {
                var label = container.Projects.Count > 1 ? $"{container.Name} ({project.Path})" : container.Name;
                toolInteractiveService.WriteLine($"{label}: {project.ProjectDefinition.Version}");
            }
        }
    }
}