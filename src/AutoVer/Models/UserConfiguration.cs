using System.Text.Json.Serialization;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;

namespace AutoVer.Models;

public class UserConfiguration
{
    [JsonIgnore] public string GitRoot { get; set; } = string.Empty;
    internal bool PersistConfiguration { get; set; }
    public List<ProjectContainer> Projects { get; set; } = [];
    public bool UseCommitsForChangelog { get; set; } = true;
    public bool UseSameVersionForAllProjects { get; set; } = false;
        
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IncrementType DefaultIncrementType { get; set; } = IncrementType.Patch;
    public Dictionary<string, string>? ChangelogCategories { get; set; }
    public bool ChangeFilesDetermineIncrementType { get; set; } = false;
}

public class ProjectContainer : IJsonOnDeserialized
{
    private IFileManager? _fileManager;
    private IPathManager? _pathManager;
    private IProjectFileHandlerResolver? _projectFileHandlerResolver;

    public required string Name { get; set; }
    public string? Path { get; set; }

    public List<string>? Paths { get; set; }
    
    internal List<Project> Projects { get; set; } = [];
        
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IncrementType? IncrementType { get; set; }
        
    public string? PrereleaseLabel { get; set; }

    public List<string> GetPaths()
    {
        if (!string.IsNullOrEmpty(Path))
        {
            return new List<string> { Path };
        }
        else
        {
            return Paths ?? [];
        }
    }

    public void OnDeserialized()
    {
        if (_fileManager == null || _pathManager == null || _projectFileHandlerResolver == null)
            return;

        bool isPathProvided = !string.IsNullOrEmpty(Path);
        bool isPathsProvided = Paths is { Count: > 0 };

        if (!isPathProvided && !isPathsProvided)
        {
            var errorMessage = $"{Name} - Either 'Path' or 'Paths' must be provided.";
            Console.WriteLine(errorMessage);
            throw new Exception(errorMessage);
        }

        if (isPathProvided && isPathsProvided)
        {
            var errorMessage = $"{Name} - 'Path' and 'Paths' cannot both be provided. Please provide only one.";
            Console.WriteLine(errorMessage);
            throw new Exception(errorMessage);
        }

        foreach (var path in GetPaths())
        {
            var normalizedPath = path.Replace('\\', _pathManager.DirectorySeparatorChar).Replace('/', _pathManager.DirectorySeparatorChar);
            if (!_fileManager.Exists(normalizedPath))
                throw new Exception($"Failed to find a valid .csproj, .nuspec, or Dockerfile file at path {normalizedPath}");

            var handler = _projectFileHandlerResolver.Resolve(normalizedPath);
            var projectDefinition = handler.Load(_pathManager.GetFullPath(normalizedPath), _fileManager.ReadAllText(normalizedPath));

            Projects.Add(new Project(normalizedPath, projectDefinition));
        }
    }

    public void InjectDependency(IFileManager fileManager, IPathManager pathManager, IProjectFileHandlerResolver projectFileHandlerResolver)
    {
        _fileManager = fileManager;
        _pathManager = pathManager;
        _projectFileHandlerResolver = projectFileHandlerResolver;
    }
}

public class Project(string path, ProjectDefinition definition)
{
    private IFileManager? _fileManager;
    private IPathManager? _pathManager;
    private IProjectFileHandlerResolver? _projectFileHandlerResolver;

    public string Path { get; set; } = path;

    internal ProjectDefinition ProjectDefinition { get; set; } = definition;

    public void OnDeserialized()
    {
        if (_fileManager == null || _pathManager == null || _projectFileHandlerResolver == null)
            return;

        var normalizedPath = Path.Replace('\\', _pathManager.DirectorySeparatorChar).Replace('/', _pathManager.DirectorySeparatorChar);
        if (!_fileManager.Exists(normalizedPath))
            throw new Exception($"Failed to find a valid .csproj, .nuspec, or Dockerfile file at path {normalizedPath}");

        var handler = _projectFileHandlerResolver.Resolve(normalizedPath);
        ProjectDefinition = handler.Load(_pathManager.GetFullPath(normalizedPath), _fileManager.ReadAllText(normalizedPath));
    }

    public void InjectDependency(IFileManager fileManager, IPathManager pathManager, IProjectFileHandlerResolver projectFileHandlerResolver)
    {
        _fileManager = fileManager;
        _pathManager = pathManager;
        _projectFileHandlerResolver = projectFileHandlerResolver;
    }
}