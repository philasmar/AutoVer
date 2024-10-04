using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ChangelogHandler(
    IGitHandler gitHandler,
    IFileManager fileManager,
    IPathManager pathManager,
    IChangeFileHandler changeFileHandler,
    IVersionHandler versionHandler) : IChangelogHandler
{
    public async Task<ChangelogEntry> GenerateChangelog(UserConfiguration configuration)
    {
        var changelogEntry = new ChangelogEntry
        {
            Title = versionHandler.GetCurrentReleaseName(configuration),
            TagName = versionHandler.GetCurrentVersionTag(configuration)
        };
        
        if (configuration.UseCommitsForChangelog)
        {
            var lastVersionTag = versionHandler.GetLastVersionTag(configuration);
            var commits = gitHandler.GetVersionCommits(configuration, lastVersionTag);

            var commitTypes = 
                commits
                    .Select(x => x.Type)
                    .Distinct()
                    .Order()
                    .ToList();
            foreach (var type in commitTypes)
            {
                var typeCommits = 
                    commits
                        .Where(x => x.Type.Equals(type))
                        .Distinct()
                        .OrderBy(x => x.Scope)
                        .ToList();
                if (configuration.ChangelogCategories != null &&
                    configuration.ChangelogCategories.TryGetValue(type, out var typeLabel))
                {
                }
                else if (ChangelogConstants.ChangelogCategories.TryGetValue(type, out typeLabel))
                {
                }
                else
                {
                    typeLabel = type;
                }

                var changelogCategory = new ChangelogCategory
                {
                    Name = typeLabel
                };
                foreach (var commit in typeCommits)
                {
                    if (string.IsNullOrEmpty(commit.Scope))
                    {
                        changelogCategory.Changes.Add(new ChangelogChange { Description = commit.Description });
                    }
                    else
                    {
                        changelogCategory.Changes.Add(new ChangelogChange { Scope = commit.Scope, Description = commit.Description });
                    }
                }
                changelogEntry.ChangelogCategories.Add(changelogCategory);
            }
            
        }
        else
        {
            var configuredProjects = new HashSet<string>();
            foreach (var project in configuration.Projects)
            {
                configuredProjects.Add(project.Name);
            }
            
            var changeFiles = await changeFileHandler.LoadChangeFilesFromRepository(configuration.GitRoot, changelogEntry.TagName);
            foreach (var changeFile in changeFiles)
            {
                changeFile.Projects.RemoveAll(x => !configuredProjects.Contains(x.Name));
            }

            if (configuration.UseSameVersionForAllProjects)
            {
                changeFiles.Add(new ChangeFile
                {
                     Projects = configuration.Projects.Select(x => new ProjectChange
                     {
                         Name = x.Name,
                         Type = x.IncrementType,
                         ChangelogMessages = new List<string>()
                     }).ToList()
                });
            }

            var changelogCategories = new Dictionary<string, ChangelogCategory>();
            foreach (var changeFile in changeFiles)
            {
                foreach (var project in changeFile.Projects)
                {
                    if (changelogCategories.TryGetValue(project.Name, out var category))
                    {
                        foreach (var changelogMessage in project.ChangelogMessages)
                        {
                            category.Changes.Add(new ChangelogChange { Description = changelogMessage });
                        }
                    }
                    else
                    {
                        var configuredProject = configuration.Projects.First(x => x.Name.Equals(project.Name));
                        if (configuredProject.ProjectDefinition is null)
                            throw new InvalidProjectException($"The project '{configuredProject.Path}' is invalid.");
                        
                        var changelogCategory = new ChangelogCategory
                        {
                            Name = configuredProject.Name,
                            Version = configuredProject.ProjectDefinition?.Version
                        };

                        if (!configuration.UseSameVersionForAllProjects)
                        {
                            if (project.ChangelogMessages.Count == 0)
                                continue;
                        }
                        
                        foreach (var changelogMessage in project.ChangelogMessages)
                        {
                            changelogCategory.Changes.Add(new ChangelogChange { Description = changelogMessage });
                        }
                        changelogEntry.ChangelogCategories.Add(changelogCategory);
                        changelogCategories.Add(configuredProject.Name, changelogCategory);
                    }
                }
            }
        }

        return changelogEntry;
    }

    public async Task PersistChangelog(UserConfiguration configuration, string changelog, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            path = pathManager.Combine(configuration.GitRoot, ChangelogConstants.DefaultChangelogFileName);
        }
        
        if (fileManager.Exists(path))
        {
            var existingChangelog = await fileManager.ReadAllTextAsync(path);
            await fileManager.WriteAllTextAsync(path, $"{changelog}\n{existingChangelog}");
        }
        else
        {
            await fileManager.WriteAllTextAsync(path, changelog);
        }
        
        gitHandler.StageChanges(configuration, path);
    }
}