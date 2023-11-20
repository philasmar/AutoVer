using AutoVer.Models;

namespace AutoVer.Services;

public interface IProjectHandler
{
    Task<List<ProjectDefinition>> GetAvailableProjects(string? projectPath);
    void UpdateVersion(ProjectDefinition projectDefinition, Increment increment = Increment.Patch);
    bool ProjectHasVersionTag(ProjectDefinition projectDefinition);
}