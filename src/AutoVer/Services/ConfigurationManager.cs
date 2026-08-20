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

        if (userConfiguration?.Projects?.Any() ?? false)
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

        return userConfiguration;
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