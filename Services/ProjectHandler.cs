using System.Xml;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public interface IProjectHandler
{
    Task<List<ProjectDefinition>> GetAvailableProjects(string projectPath);
}

public class ProjectHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager
    ) : IProjectHandler
{
    public async Task<List<ProjectDefinition>> GetAvailableProjects(string projectPath)
    {
        var projectPaths = new List<string>();
        
        if (directoryManager.Exists(projectPath))
        {
            projectPath = directoryManager.GetDirectoryInfo(projectPath).FullName;
            var files = directoryManager.GetFiles(projectPath, "*.csproj").ToList();
            foreach (var file in files)
            {
                var newPath = Path.Combine(projectPath, file);
                if (fileManager.Exists(newPath))
                    projectPaths.Add(newPath);
            }
        }

        if (!projectPaths.Any())
        {
            throw new Exception($"Failed to find a valid .csproj file at path {projectPath}");
        }

        var projectDefinitions = new List<ProjectDefinition>();

        foreach (var project in projectPaths)
        {
            var extension = Path.GetExtension(project);
            if (!string.Equals(extension, ".csproj"))
            {
                var errorMessage = $"Invalid project path {project}. The project path must point to a .csproj file";
                throw new Exception(errorMessage);
            }
            
            var xmlProjectFile = new XmlDocument();
            xmlProjectFile.LoadXml(await fileManager.ReadAllTextAsync(project));
            
            var projectDefinition =  new ProjectDefinition(
                xmlProjectFile,
                project
            );
            
            var version = xmlProjectFile.GetElementsByTagName("Version");
            if (version.Count > 0)
            {
                projectDefinition.Version = version[0]?.InnerText;
            }
            
            projectDefinitions.Add(projectDefinition);
        }

        return projectDefinitions;
    }
}