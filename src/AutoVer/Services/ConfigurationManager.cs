using System.Text.Json;
using System.Text.Json.Serialization;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.Converters;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;

namespace AutoVer.Services;

public class ConfigurationManager(
    IFileManager fileManager,
    IPathManager pathManager,
    IGitHandler gitHandler,
    IProjectHandler projectHandler,
    IChangeFileHandler changeFileHandler,
    IProjectFileHandlerResolver projectFileHandlerResolver,
    ICurrentDirectoryContext currentDirectoryContext) : IConfigurationManager
{
    private async Task<UserConfiguration?> LoadUserConfigurationFromRepository(string repositoryRoot, string? tagName = null)
    {
        var configPath = string.Empty;
        
        try
        {
            if (string.IsNullOrEmpty(tagName))
            {
                configPath = pathManager.Combine(repositoryRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
                if (!fileManager.Exists(configPath))
                    return null;
            
                var content = await fileManager.ReadAllBytesAsync(configPath);
                await using var stream = new MemoryStream(content);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new UserConfigurationConverter(fileManager, pathManager, projectFileHandlerResolver));
                var userConfiguration = await JsonSerializer.DeserializeAsync<UserConfiguration>(stream, options);

                return userConfiguration;
            }
            else
            {
                // The live filesystem isn't the right thing to check here - the config can
                // exist on disk today while not yet existing at the given tag (e.g. autover.json
                // was added after the last release). GetFileByTag itself reports that as null.
                configPath = pathManager.Combine(ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
                var fileContent = gitHandler.GetFileByTag(repositoryRoot, tagName, configPath);
                if (fileContent is null)
                    return null;
                var options = new JsonSerializerOptions();
                options.Converters.Add(new UserConfigurationConverter(fileManager, pathManager, projectFileHandlerResolver));
                var userConfiguration =  JsonSerializer.Deserialize<UserConfiguration>(fileContent, options);
                
                return userConfiguration;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidUserConfigurationException(
                $"There was an issue loading the user configuration at '{configPath}'.", 
                ex);
        }
    }

    public async Task<UserConfiguration> RetrieveUserConfiguration(string? projectPath, IncrementType incrementType, string? tagName = null)
    {
        if (string.IsNullOrEmpty(projectPath))
            projectPath = Directory.GetCurrentDirectory();
        var gitRoot = gitHandler.FindGitRootDirectory(projectPath);

        // autover.json's project paths (and the .autover/changes folder) are relative to the
        // git root, not to --project-path — which is free to point at any subdirectory of the
        // repo, since FindGitRootDirectory walks up from it. Re-point the shared current
        // directory at the git root now, before anything resolves one of those relative paths.
        currentDirectoryContext.SetCurrentDirectory(gitRoot);

        var userConfiguration = await LoadUserConfigurationFromRepository(gitRoot, tagName);

        // A tag-scoped lookup can legitimately come back empty because the config was only
        // added/changed after that tag was created (e.g. adopting change files on a repo that
        // already has releases). That's "no config at the tag", not "no config at all" - fall
        // back to the current on-disk config so real settings like UseCommitsForChangelog don't
        // silently revert to the model's bare defaults.
        if (userConfiguration is null && !string.IsNullOrEmpty(tagName))
            userConfiguration = await LoadUserConfigurationFromRepository(gitRoot);

        // Discovery is also skipped when the version comes from the repository's tags: there are no
        // project files to find, so scanning for them would fail with a message about missing
        // .csproj/.nuspec/Dockerfile files instead of the real problem.
        if ((userConfiguration?.Projects?.Any() ?? false) || (userConfiguration?.VersionFromTag ?? false))
        {
            userConfiguration.GitRoot = gitRoot;
            userConfiguration.PersistConfiguration = true;
        }
        else
        {
            // Only discover projects under --project-path when there's no existing config to
            // fall back to. An existing autover.json already fully describes the projects
            // (which can live anywhere in the repo), so --project-path pointing at some other
            // subdirectory — e.g. the one the user happens to be running from — must not force
            // a (possibly failing) discovery scan that isn't even needed.
            var availableProjects = await projectHandler.GetAvailableProjects(projectPath);

            userConfiguration ??= new();
            // GitRoot must be backfilled unconditionally here, not only when userConfiguration
            // was null — an existing config with an empty (but non-null) Projects list, e.g.
            // one that simply omits "Projects", takes this branch too and would otherwise be
            // left with GitRoot's [JsonIgnore] default of "", failing the check below even
            // though the repo and discovery both succeeded.
            userConfiguration.GitRoot = gitRoot;

            if (userConfiguration.Projects is null)
                userConfiguration.Projects = [];

            var projectNames = GetUniqueProjectNames(availableProjects);
            foreach (var project in availableProjects)
            {
                userConfiguration.Projects.Add(new ProjectContainer
                {
                    Name = projectNames[project.ProjectPath],
                    Path = project.ProjectPath,
                    Projects = [new(project.ProjectPath, project)],
                    IncrementType = incrementType
                });
            }
        }
        
        if (string.IsNullOrEmpty(userConfiguration.GitRoot))
            throw new InvalidProjectException("The project path you have specified is not a valid git repository.");

        ValidateTagFormats(userConfiguration);
        ValidateVersionFromTag(userConfiguration);

        return userConfiguration;
    }

    public UserConfiguration? LoadRepositorySettings(string gitRoot)
    {
        var configPath = pathManager.Combine(gitRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
        if (!fileManager.Exists(configPath))
            return null;

        try
        {
            // Deliberately deserialized without UserConfigurationConverter: callers here only need
            // repository-level settings, and must not trigger project-file loading (or the path
            // validation that comes with it) as a side effect of reading one.
            return JsonSerializer.Deserialize<UserConfiguration>(fileManager.ReadAllText(configPath));
        }
        catch (Exception ex)
        {
            throw new InvalidUserConfigurationException(
                $"There was an issue loading the user configuration at '{configPath}'.",
                ex);
        }
    }

    /// <summary>
    /// Fails fast on a tag/release-name format that can't work, so a bad format is reported when the
    /// configuration is read rather than part-way through a release.
    /// </summary>
    private void ValidateTagFormats(UserConfiguration userConfiguration)
    {
        var tagFormat = VersionTagFormat.Parse(
            userConfiguration.EffectiveTagFormat,
            nameof(UserConfiguration.TagFormat));
        var releaseNameFormat = VersionTagFormat.Parse(
            userConfiguration.ResolveReleaseNameFormat(tagFormat.Family),
            nameof(UserConfiguration.ReleaseNameFormat));

        // Rendered with stand-in values purely to test the format's own literal text against git's
        // ref grammar, using git itself rather than a hand-maintained copy of the rules. Done here,
        // before any project file is written, so a format git would refuse doesn't leave the working
        // tree half-updated. VersionHandler repeats the check on the real tag name, where an illegal
        // character could still arrive through a placeholder value such as a prerelease label.
        var probeTagName = tagFormat.Render(
            new ThreePartVersion { Major = 1, Minor = 0, Patch = 0, PrereleaseLabel = "0" },
            new DateTime(2000, 1, 1),
            2);
        if (!gitHandler.IsValidTagName(probeTagName))
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.TagFormat)}' ('{tagFormat.Format}') produces tag names git will not " +
                $"accept, such as '{probeTagName}'. A tag name cannot contain a space, '~', '^', ':', '?', '*', " +
                "'[', '\\', '..' or '@{', cannot end with '.' or '.lock', and cannot have an empty path segment.");

        // A release name is rendered from the components of the tag it describes, so it can only
        // ask for components the tag actually carries - a date-based name has nothing to render
        // from once the tag holds a version instead of a date.
        if (tagFormat.Family != releaseNameFormat.Family)
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.TagFormat)}' is {tagFormat.Family.ToString().ToLowerInvariant()}-based " +
                $"('{tagFormat.Format}') while '{nameof(UserConfiguration.ReleaseNameFormat)}' is " +
                $"{releaseNameFormat.Family.ToString().ToLowerInvariant()}-based ('{releaseNameFormat.Format}'). " +
                "Both must use the same family of placeholders.");

        // Exempt when the version comes from the tag: every project then reads that one version, so
        // they cannot disagree in the way this rule guards against.
        if (tagFormat.Family == TagFormatFamily.Semver &&
            !userConfiguration.VersionFromTag &&
            !userConfiguration.UseSameVersionForAllProjects &&
            userConfiguration.Projects.Count > 1)
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.TagFormat)}' is version-based ('{tagFormat.Format}') but this repository " +
                $"has {userConfiguration.Projects.Count} projects that can hold different versions " +
                $"('{nameof(UserConfiguration.UseSameVersionForAllProjects)}' is false), so there's no single version " +
                "for the tag to represent. Either set 'UseSameVersionForAllProjects' to true, or use a date-based " +
                $"'{nameof(UserConfiguration.TagFormat)}'.");
    }


    public async Task ResetUserConfiguration(UserConfiguration userConfiguration, UserConfigurationResetRequest resetRequest)
    {
        var configPath = pathManager.Combine(userConfiguration.GitRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName);
        if (!fileManager.Exists(configPath))
            return;

        try
        {
            foreach (var project in userConfiguration.Projects)
            {
                if (resetRequest.Changelog)
                    changeFileHandler.ResetChangeFiles(userConfiguration);
                
                if (resetRequest.IncrementType)
                    project.IncrementType = userConfiguration.DefaultIncrementType;
            }

            await using (var stream = new FileStream(configPath, FileMode.Create))
            {
                await using (var sw = new StreamWriter(stream))
                {
                    await JsonSerializer.SerializeAsync(
                        sw.BaseStream, 
                        userConfiguration, 
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        });
                }
            }
            
            gitHandler.StageChanges(userConfiguration, configPath);
        }
        catch (Exception ex)
        {
            throw new ResetUserConfigurationFailedException(
                $"Unable to reset the configuration file '{configPath}'.",
                ex);
        }
    }

    /// <summary>
    /// Checks the combination of settings a tag-sourced version depends on, at load time rather than
    /// part-way through a release.
    /// </summary>
    private static void ValidateVersionFromTag(UserConfiguration userConfiguration)
    {
        // InitialVersion applies to any repository - it is also what seeds a project file that
        // doesn't carry a version yet - so it is validated regardless of where the version comes from.
        if (!ThreePartVersion.TryParse(userConfiguration.EffectiveInitialVersion, out _))
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.InitialVersion)}' ('{userConfiguration.EffectiveInitialVersion}') is not " +
                "a valid three part version.");

        if (!userConfiguration.VersionFromTag)
            return;



        // The tag is the only place the version lives, so it has to be able to carry one - a
        // date-based tag would leave nothing to read the current version back from.
        var tagFormat = VersionTagFormat.Parse(
            userConfiguration.EffectiveTagFormat,
            nameof(UserConfiguration.TagFormat));
        if (tagFormat.Family != TagFormatFamily.Semver)
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.VersionFromTag)}' needs a version-based " +
                $"'{nameof(UserConfiguration.TagFormat)}' to read the version back from, but " +
                $"'{tagFormat.Format}' is date-based. Use a format built from {{major}}, {{minor}} and " +
                "{patch}, e.g. 'v{major}.{minor}.{patch}'.");

        // The tag is the only place a tag-sourced version lives, so a prerelease label the format
        // can't render isn't merely absent from the tag - it's lost outright, and every release would
        // silently come out as a plain version. A file-backed project at least keeps it in the file.
        var prereleaseProjects = userConfiguration.Projects
            .Where(project => !string.IsNullOrEmpty(project.PrereleaseLabel))
            .Select(project => project.Name)
            .ToList();
        if (prereleaseProjects.Count > 0 && !tagFormat.SupportsPrerelease)
            throw new InvalidUserConfigurationException(
                $"{string.Join(", ", prereleaseProjects.Select(name => $"'{name}'"))} sets a PrereleaseLabel, but " +
                $"'{nameof(UserConfiguration.TagFormat)}' ('{tagFormat.Format}') has no {{prerelease}} placeholder to " +
                "carry it - and with the version coming from the tag, there is nowhere else for it to live. Add an " +
                "optional prerelease group, e.g. 'v{major}.{minor}.{patch}[-{prerelease}]'.");

        if (userConfiguration.Projects.Count == 0)
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.VersionFromTag)}' is set but no projects are listed. List at least " +
                "one project by name, so a change file has something to attach to and the changelog has something " +
                "to label, e.g. \"Projects\": [ { \"Name\": \"my-repo\" } ].");

        // One tag carries one version, so a project reading its version from a file alongside one
        // reading it from the tag would leave no single answer for what the tag represents.
        var withPaths = userConfiguration.Projects
            .Where(project => project.GetPaths().Count > 0)
            .Select(project => project.Name)
            .ToList();
        if (withPaths.Count > 0)
            throw new InvalidUserConfigurationException(
                $"'{nameof(UserConfiguration.VersionFromTag)}' is set, so every project takes its version from the " +
                $"repository's tags - but {string.Join(", ", withPaths.Select(name => $"'{name}'"))} still " +
                "specifies a Path or Paths. Remove them, or turn " +
                $"'{nameof(UserConfiguration.VersionFromTag)}' off.");

    }

    private string GetProjectName(string projectPath) =>
        projectFileHandlerResolver.Resolve(projectPath).GetDisplayName(projectPath);

    // A handler's display name only sees one path at a time and can't tell whether it
    // collides with a sibling (e.g. two auto-discovered "Dockerfile"s in different
    // folders). Disambiguate any collisions here, where every discovered project is visible
    // at once, by prefixing progressively more of the parent directory path — one segment
    // at a time — until every name is unique. Since every project's full path is guaranteed
    // unique on disk, this always converges; the full path is a last-resort fallback in case
    // it somehow doesn't.
    private Dictionary<string, string> GetUniqueProjectNames(List<ProjectDefinition> projects)
    {
        var baseNames = projects.ToDictionary(project => project.ProjectPath, project => GetProjectName(project.ProjectPath));
        var names = new Dictionary<string, string>(baseNames);

        var maxDepth = projects.Count == 0
            ? 0
            : projects.Max(project => project.ProjectPath.Split(pathManager.DirectorySeparatorChar).Length);

        for (var depth = 1; depth <= maxDepth && HasDuplicates(names); depth++)
        {
            var duplicateNames = GetDuplicateNames(names);
            foreach (var project in projects)
            {
                if (!duplicateNames.Contains(names[project.ProjectPath]))
                    continue;

                var prefix = GetParentDirectorySegments(project.ProjectPath, depth);
                names[project.ProjectPath] = string.IsNullOrEmpty(prefix) ? baseNames[project.ProjectPath] : $"{prefix}-{baseNames[project.ProjectPath]}";
            }
        }

        // Guaranteed-unique fallback for any name that's still colliding once every directory
        // segment has been exhausted — scoped to only the still-duplicate entries, so it never
        // disturbs the friendly names already resolved for everything else.
        if (HasDuplicates(names))
        {
            var remainingDuplicateNames = GetDuplicateNames(names);
            foreach (var project in projects)
            {
                if (remainingDuplicateNames.Contains(names[project.ProjectPath]))
                    names[project.ProjectPath] = project.ProjectPath;
            }
        }

        return names;
    }

    private static bool HasDuplicates(Dictionary<string, string> names) =>
        names.Values.GroupBy(name => name).Any(group => group.Count() > 1);

    private static HashSet<string> GetDuplicateNames(Dictionary<string, string> names) =>
        names.Values.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();

    // The last `depth` directory segments immediately above the file itself, joined with '-'.
    private string GetParentDirectorySegments(string projectPath, int depth)
    {
        var parts = projectPath.Split(pathManager.DirectorySeparatorChar);
        var directoryParts = parts.Take(parts.Length - 1).ToArray();
        var take = Math.Min(depth, directoryParts.Length);
        return string.Join('-', directoryParts.Skip(directoryParts.Length - take));
    }
}