using AutoVer.Models;

namespace AutoVer.Services;

public interface IProjectHandler
{
    Task<List<ProjectDefinition>> GetAvailableProjects(string? projectPath);
    void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType = IncrementType.Patch);
    bool ProjectHasVersionTag(ProjectDefinition projectDefinition);
}