using AutoVer.Services;

namespace AutoVer.Commands;

public class InfoCommand(
    IProjectHandler projectHandler,
    IToolInteractiveService toolInteractiveService,
    IVersionIncrementer versionIncrementer,
    IGitHandler gitHandler)
{
    public async Task ExecuteAsync(string projectPath)
    {
        var sourceControlRootDirectory = gitHandler.FindGitRootDirectory(projectPath);
        var availableProjects = await projectHandler.GetAvailableProjects(projectPath);
        if (availableProjects.Count == 1)
        {
            var version = availableProjects.First().Version;
            toolInteractiveService.WriteLine($"Current version {versionIncrementer.GetCurrentVersion(version)}");
            toolInteractiveService.WriteLine($"Next version {versionIncrementer.GetNextVersion(version)}");
        }
        else
        {
            foreach (var availableProject in availableProjects)
            {
                toolInteractiveService.WriteLine(availableProject.ProjectPath);
                toolInteractiveService.WriteLine($"\tCurrent version {versionIncrementer.GetCurrentVersion(availableProject.Version)}");
                toolInteractiveService.WriteLine($"\tNext version {versionIncrementer.GetNextVersion(availableProject.Version)}");
            }
        }
    }
}