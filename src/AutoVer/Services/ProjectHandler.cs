using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;

namespace AutoVer.Services;

public class ProjectHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager,
    IPathManager pathManager,
    IProjectFileHandlerResolver projectFileHandlerResolver
    ) : IProjectHandler
{
    public async Task<List<ProjectDefinition>> GetAvailableProjects(string? projectPath)
    {
        var projectPaths = new List<string>();
        var seenProjectPaths = new HashSet<string>();

        if (!string.IsNullOrEmpty(projectPath) && directoryManager.Exists(projectPath))
        {
            projectPath = directoryManager.GetDirectoryInfo(projectPath).FullName;
            foreach (var searchPattern in projectFileHandlerResolver.SearchPatterns)
            {
                var files = directoryManager.GetFiles(projectPath, searchPattern, SearchOption.AllDirectories).ToList();
                foreach (var file in files)
                {
                    var newPath = pathManager.Combine(projectPath, file);

                    // Different handlers' search patterns can overlap for the same file (e.g. the
                    // OS wildcard matcher treats "Dockerfile.*" as matching the extensionless
                    // "Dockerfile" itself), and patterns are glob-based so they can also match
                    // files no handler actually owns (e.g. a stray "Dockerfile.orig" backup).
                    // Dedupe with a seen-set but keep insertion order deterministic, and skip
                    // anything that doesn't truly resolve to a handler.
                    if (fileManager.Exists(newPath) &&
                        seenProjectPaths.Add(newPath) &&
                        projectFileHandlerResolver.TryResolve(newPath, out _))
                    {
                        projectPaths.Add(newPath);
                    }
                }
            }
        }

        if (projectPaths.Count == 0)
        {
            throw new InvalidProjectException($"Failed to find a valid .csproj, .nuspec, or Dockerfile file at path {projectPath}");
        }

        var projectDefinitions = new List<ProjectDefinition>();

        foreach (var project in projectPaths)
        {
            var handler = projectFileHandlerResolver.Resolve(project);
            var rawContent = await fileManager.ReadAllTextAsync(project);
            projectDefinitions.Add(handler.Load(project, rawContent));
        }

        return projectDefinitions;
    }

    public async Task<ProjectDefinition> GetProjectDefinition(string projectPath)
    {
        var normalizedPath = projectPath.Replace('\\', pathManager.DirectorySeparatorChar).Replace('/', pathManager.DirectorySeparatorChar);
        if (!fileManager.Exists(normalizedPath))
            throw new InvalidProjectException($"Failed to find a valid .csproj, .nuspec, or Dockerfile file at path {normalizedPath}");

        var handler = projectFileHandlerResolver.Resolve(normalizedPath);
        var fullPath = pathManager.GetFullPath(normalizedPath);
        var rawContent = await fileManager.ReadAllTextAsync(normalizedPath);
        return handler.Load(fullPath, rawContent);
    }

    public void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null)
    {
        var handler = projectFileHandlerResolver.Resolve(projectDefinition.ProjectPath);
        handler.UpdateVersion(projectDefinition, incrementType, prereleaseLabel, overrideVersion);
    }
}
