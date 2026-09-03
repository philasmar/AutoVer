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

        // A project carrying no version yet is seeded rather than rejected: the element is created,
        // and the version it gets is taken as-is rather than incremented from nothing.
        var seeded = !versionTagList.Any();
        var versionTag = seeded
            ? CreateVersionElement(xmlProjectFile, versionTagName, projectDefinition.ProjectPath)
            : versionTagList.First();

        if (string.IsNullOrEmpty(overrideVersion))
        {
            var nextVersion = seeded
                // The caller normally supplies the version to seed with; this is only the fallback
                // for a direct call that didn't.
                ? versionIncrementer.GetCurrentVersion(null)
                : versionIncrementer.GetNextVersion(versionTag.InnerText, incrementType, prereleaseLabel);
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

        // Keep the in-memory definition in step with what was just written - a version-based tag
        // format is rendered from this after every project has been updated.
        projectDefinition.Version = versionTag.InnerText;

        using var stream = fileManager.OpenWrite(projectDefinition.ProjectPath);
        xmlProjectFile.Save(stream);
    }

    /// <summary>
    /// Creates the version element for a project that doesn't have one. A nuspec's version belongs
    /// in its metadata element; a project file's belongs in a PropertyGroup, and specifically in an
    /// unconditioned one - a version inside a Condition would only apply to some builds.
    /// </summary>
    private static XmlNode CreateVersionElement(XmlDocument document, string versionTagName, string projectPath)
    {
        var root = document.DocumentElement
            ?? throw new InvalidProjectException($"The project '{projectPath}' has no root element.");

        XmlNode parent;
        if (versionTagName.Equals(ProjectConstants.NuspecVersionTag, StringComparison.Ordinal))
        {
            parent = document.GetElementsByTagName("metadata").Cast<XmlNode>().FirstOrDefault()
                ?? throw new InvalidProjectException(
                    $"The nuspec '{projectPath}' has no 'metadata' element to add a '{ProjectConstants.NuspecVersionTag}' to.");
        }
        else
        {
            var unconditioned = document.GetElementsByTagName("PropertyGroup").Cast<XmlNode>()
                .FirstOrDefault(group => group.Attributes?["Condition"] is null);

            if (unconditioned is null)
            {
                unconditioned = document.CreateElement("PropertyGroup", root.NamespaceURI);
                InsertAsLastElement(document, root, unconditioned);
            }

            parent = unconditioned;
        }

        var element = document.CreateElement(versionTagName, parent.NamespaceURI);
        InsertAsLastElement(document, parent, element);
        return element;
    }

    /// <summary>
    /// Adds a node as the last element child of its parent while keeping the file's layout. The
    /// document is loaded with PreserveWhitespace, so indentation is made of real text nodes: the
    /// whitespace before the closing tag is a node of its own, and simply appending would put the new
    /// element after it and leave the closing tag on the same line.
    /// </summary>
    private static void InsertAsLastElement(XmlDocument document, XmlNode parent, XmlNode node)
    {
        static bool IsWhitespace(XmlNode? candidate) =>
            candidate is { NodeType: XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace };

        // The indentation an existing sibling sits behind, so the new element lines up with it
        // rather than with the closing tag.
        var firstElement = parent.ChildNodes.Cast<XmlNode>()
            .FirstOrDefault(child => child.NodeType == XmlNodeType.Element);
        var indent = IsWhitespace(firstElement?.PreviousSibling) ? firstElement!.PreviousSibling!.Value : null;

        if (IsWhitespace(parent.LastChild))
        {
            parent.InsertBefore(node, parent.LastChild!);
            if (!string.IsNullOrEmpty(indent))
                parent.InsertBefore(document.CreateWhitespace(indent), node);

            return;
        }

        if (!string.IsNullOrEmpty(indent))
            parent.AppendChild(document.CreateWhitespace(indent));

        parent.AppendChild(node);
    }

    private static string GetVersionTagName(string projectPath)
    {
        var extension = Path.GetExtension(projectPath);
        return string.Equals(extension, ".nuspec", StringComparison.OrdinalIgnoreCase)
            ? ProjectConstants.NuspecVersionTag
            : ProjectConstants.VersionTag;
    }
}
