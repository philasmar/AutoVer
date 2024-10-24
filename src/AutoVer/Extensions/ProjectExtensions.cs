using System.Xml;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.Extensions;

public static class ProjectExtensions
{
    public static void EnsureProjectHasVersionTag(this ProjectContainer projectContainer)
    {
        foreach (var project in projectContainer.Projects)
        {
            var extension = Path.GetExtension(project.ProjectDefinition.ProjectPath);
            var versionTag = ProjectConstants.VersionTag;
            if (string.Equals(extension, ".nuspec"))
                versionTag = ProjectConstants.NuspecVersionTag;
            var versionTagList = project.ProjectDefinition.Contents.GetElementsByTagName(versionTag).Cast<XmlNode>().ToList();
            if (!versionTagList.Any())
                throw new NoVersionTagException($"The project '{projectContainer.Name}' does not have a '{versionTag}' tag. Add a '{versionTag}' tag and run the tool again.");
        }
    }
}