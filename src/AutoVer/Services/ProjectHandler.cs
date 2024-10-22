using System.Xml;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services;

public class ProjectHandler(
    IDirectoryManager directoryManager,
    IFileManager fileManager,
    IPathManager pathManager,
    IVersionIncrementer versionIncrementer
    ) : IProjectHandler
{
    public async Task<List<ProjectDefinition>> GetAvailableProjects(string? projectPath)
    {
        var projectPaths = new List<string>();
        
        if (!string.IsNullOrEmpty(projectPath) && directoryManager.Exists(projectPath))
        {
            projectPath = directoryManager.GetDirectoryInfo(projectPath).FullName;
            var projectPatterns = new List<string> { "*.csproj", "*.nuspec" };
            foreach (var projectPattern in projectPatterns)
            {
                var files = directoryManager.GetFiles(projectPath, projectPattern, SearchOption.AllDirectories).ToList();
                foreach (var file in files)
                {
                    var newPath = pathManager.Combine(projectPath, file);
                    if (fileManager.Exists(newPath))
                        projectPaths.Add(newPath);
                }
            }
        }

        if (projectPaths.Count == 0)
        {
            throw new Exception($"Failed to find a valid .csproj or .nuspec file at path {projectPath}");
        }

        var projectDefinitions = new List<ProjectDefinition>();

        foreach (var project in projectPaths)
        {
            var extension = pathManager.GetExtension(project);
            if (!string.Equals(extension, ".csproj") && !string.Equals(extension, ".nuspec"))
            {
                var errorMessage = $"Invalid project path {project}. The project path must point to a .csproj or .nuspec file";
                throw new Exception(errorMessage);
            }
            
            var xmlProjectFile = new XmlDocument{ PreserveWhitespace = true };
            xmlProjectFile.LoadXml(await fileManager.ReadAllTextAsync(project));
            
            var projectDefinition =  new ProjectDefinition(
                xmlProjectFile,
                project
            );

            var versionTag = ProjectConstants.VersionTag;
            if (string.Equals(extension, ".nuspec"))
                versionTag = ProjectConstants.NuspecVersionTag;
            var version = xmlProjectFile.GetElementsByTagName(versionTag);
            if (version.Count > 0)
            {
                projectDefinition.Version = version[0]?.InnerText;
            }
            
            projectDefinitions.Add(projectDefinition);
        }

        return projectDefinitions;
    }

    public void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null)
    {
        var extension = pathManager.GetExtension(projectDefinition.ProjectPath);
        var versionTagName = ProjectConstants.VersionTag;
        if (string.Equals(extension, ".nuspec"))
            versionTagName = ProjectConstants.NuspecVersionTag;
        var versionTagList = projectDefinition.Contents.GetElementsByTagName(versionTagName).Cast<XmlNode>().ToList();
        if (!versionTagList.Any())
            throw new NoVersionTagException($"The project '{projectDefinition.ProjectPath}' does not have a {ProjectConstants.VersionTag} tag. Add a {ProjectConstants.VersionTag} tag and run the tool again.");
        
        var versionTag = versionTagList.First();
        if (string.IsNullOrEmpty(overrideVersion))
        {
            var nextVersion = versionIncrementer.GetNextVersion(versionTag.InnerText, incrementType, prereleaseLabel);
            versionTag.InnerText = nextVersion.ToString();
        }
        else
        {
            if (ThreePartVersion.TryParse(overrideVersion, out var version))
            {
                versionTag.InnerText = version.ToString();
            }
            else
            {
                throw new InvalidArgumentException($"The version '{overrideVersion}' you are trying to update to is invalid.");
            }
        }
        
        projectDefinition.Contents.Save(projectDefinition.ProjectPath);
    }

    public bool ProjectHasVersionTag(ProjectDefinition projectDefinition)
    {
        var extension = pathManager.GetExtension(projectDefinition.ProjectPath);
        var versionTag = ProjectConstants.VersionTag;
        if (string.Equals(extension, ".nuspec"))
            versionTag = ProjectConstants.NuspecVersionTag;
        var versionTagList = projectDefinition.Contents.GetElementsByTagName(versionTag).Cast<XmlNode>().ToList();
        return versionTagList.Any();
    }
}