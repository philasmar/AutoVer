using System.Xml;

namespace AutoVer.Models;

public class ProjectDefinition(
    XmlDocument contents,
    string projectPath)
{
    /// <summary>
    /// Xml file contents of the Project file.
    /// </summary>
    public XmlDocument Contents { get; set; } = contents;

    /// <summary>
    /// Full path to the project file
    /// </summary>
    public string ProjectPath { get; set; } = projectPath;

    /// <summary>
    /// Project version
    /// </summary>
    public string? Version { get; set; }
}