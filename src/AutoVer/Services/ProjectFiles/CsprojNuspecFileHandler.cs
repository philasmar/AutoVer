using System.Xml;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services.ProjectFiles;

public class CsprojNuspecFileHandler(
    IVersionIncrementer versionIncrementer,
    IFileManager fileManager) : IProjectFileHandler
{
    public IEnumerable<string> SearchPatterns => new[] { "*.csproj", "*.nuspec" };

    public bool IsMatch(string projectPath)
    {
        var extension = Path.GetExtension(projectPath);
        return string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".nuspec", StringComparison.OrdinalIgnoreCase);
    }

    public string GetDisplayName(string projectPath) => Path.GetFileNameWithoutExtension(projectPath);

    public ProjectDefinition Load(string projectPath, string rawContent)
    {
        var xmlProjectFile = new XmlDocument { PreserveWhitespace = true };
        xmlProjectFile.LoadXml(rawContent);

        var projectDefinition = new ProjectDefinition(xmlProjectFile, projectPath);

        var version = xmlProjectFile.GetElementsByTagName(GetVersionTagName(projectPath));
        if (version.Count > 0)
        {
            projectDefinition.Version = version[0]?.InnerText;
        }

        return projectDefinition;
    }

    public void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null)
    {
        var xmlProjectFile = projectDefinition.GetContents<XmlDocument>();
        var versionTagName = GetVersionTagName(projectDefinition.ProjectPath);
        var versionTagList = xmlProjectFile.GetElementsByTagName(versionTagName).Cast<XmlNode>().ToList();
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

        using var stream = fileManager.OpenWrite(projectDefinition.ProjectPath);
        xmlProjectFile.Save(stream);
    }

    private static string GetVersionTagName(string projectPath)
    {
        var extension = Path.GetExtension(projectPath);
        return string.Equals(extension, ".nuspec", StringComparison.OrdinalIgnoreCase)
            ? ProjectConstants.NuspecVersionTag
            : ProjectConstants.VersionTag;
    }
}
