using AutoVer.Exceptions;

namespace AutoVer.Services.ProjectFiles;

public class ProjectFileHandlerResolver(
    IEnumerable<IProjectFileHandler> handlers) : IProjectFileHandlerResolver
{
    public IEnumerable<string> SearchPatterns => handlers.SelectMany(handler => handler.SearchPatterns).Distinct();

    public IProjectFileHandler Resolve(string projectPath)
    {
        if (!TryResolve(projectPath, out var handler))
            throw new InvalidProjectException($"Invalid project path {projectPath}. The project path must point to a .csproj, .nuspec, or Dockerfile file.");

        return handler!;
    }

    public bool TryResolve(string projectPath, out IProjectFileHandler? handler)
    {
        handler = handlers.FirstOrDefault(handler => handler.IsMatch(projectPath));
        return handler is not null;
    }
}
