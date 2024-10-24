using AutoVer.Models;

namespace AutoVer.Services;

public interface IProjectHandler
{
    Task<List<ProjectDefinition>> GetAvailableProjects(string? projectPath);
    Task<ProjectDefinition> GetProjectDefinition(string projectPath);
    void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null);
}