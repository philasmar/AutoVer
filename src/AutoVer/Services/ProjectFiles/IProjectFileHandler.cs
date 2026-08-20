using AutoVer.Models;

namespace AutoVer.Services.ProjectFiles;

/// <summary>
/// Handles reading and writing the version of one project file type (e.g. .csproj, .nuspec, Dockerfile).
/// </summary>
public interface IProjectFileHandler
{
    /// <summary>
    /// Glob patterns used to discover candidate files of this type under a directory.
    /// </summary>
    IEnumerable<string> SearchPatterns { get; }

    /// <summary>
    /// Whether this handler owns the given file, based on its name/extension.
    /// </summary>
    bool IsMatch(string projectPath);

    /// <summary>
    /// The display name to use for this project when none is configured explicitly
    /// (e.g. the file name with its extension stripped for .csproj/.nuspec).
    /// </summary>
    string GetDisplayName(string projectPath);

    ProjectDefinition Load(string projectPath, string rawContent);

    void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null);
}
