using System.Xml;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ProjectHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager,
    IVersionIncrementer versionIncrementer
    ) : IProjectHandler
{
    public async Task<List<ProjectDefinition>> GetAvailableProjects(string? projectPath)
    {
        var projectPaths = new List<string>();
        
        if (!string.IsNullOrEmpty(projectPath) && directoryManager.Exists(projectPath))
        {
            projectPath = directoryManager.GetDirectoryInfo(projectPath).FullName;
            var files = directoryManager.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories).ToList();
            foreach (var file in files)
            {
                var newPath = Path.Combine(projectPath, file);
                if (fileManager.Exists(newPath))
                    projectPaths.Add(newPath);
            }
        }

        if (projectPaths.Count == 0)
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
            
            var xmlProjectFile = new XmlDocument{ PreserveWhitespace = true };
            xmlProjectFile.LoadXml(await fileManager.ReadAllTextAsync(project));
            
            var projectDefinition =  new ProjectDefinition(
                xmlProjectFile,
                project
            );
            
            var version = xmlProjectFile.GetElementsByTagName(ProjectConstants.VersionTag);
            if (version.Count > 0)
            {
                projectDefinition.Version = version[0]?.InnerText;
            }
            
            projectDefinitions.Add(projectDefinition);
        }

        return projectDefinitions;
    }

    public void UpdateVersion(ProjectDefinition projectDefinition, Increment increment)
    {
        var versionTagList = projectDefinition.Contents.GetElementsByTagName(ProjectConstants.VersionTag).Cast<XmlNode>().ToList();
        if (!versionTagList.Any())
            throw new NoVersionTagException($"The project '{projectDefinition.ProjectPath}' does not have a {ProjectConstants.VersionTag} tag. Add a {ProjectConstants.VersionTag} tag and run the tool again.");

        var versionTag = versionTagList.First();
        var nextVersion = versionIncrementer.GetNextVersion(versionTag.InnerText, increment);
        versionTag.InnerText = nextVersion.ToString();
        
        projectDefinition.Contents.Save(projectDefinition.ProjectPath);
    }

    public bool ProjectHasVersionTag(ProjectDefinition projectDefinition)
    {
        var versionTagList = projectDefinition.Contents.GetElementsByTagName(ProjectConstants.VersionTag).Cast<XmlNode>().ToList();
        return versionTagList.Any();
    }
}