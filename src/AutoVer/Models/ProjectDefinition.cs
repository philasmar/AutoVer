namespace AutoVer.Models;

public class ProjectDefinition(
    object contents,
    string projectPath)
{
    /// <summary>
    /// Parsed contents of the project file. The concrete type is owned by whichever
    /// IProjectFileHandler produced this instance (e.g. XmlDocument for .csproj/.nuspec,
    /// List&lt;string&gt; of lines for a Dockerfile).
    /// </summary>
    public object Contents { get; set; } = contents;

    /// <summary>
    /// Full path to the project file
    /// </summary>
    public string ProjectPath { get; set; } = projectPath;

    /// <summary>
    /// Project version
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Casts <see cref="Contents"/> to the type its owning IProjectFileHandler produced,
    /// throwing a clear error instead of a bare InvalidCastException if a ProjectDefinition
    /// ever ends up routed to the wrong handler.
    /// </summary>
    public T GetContents<T>()
    {
        if (Contents is not T typed)
            throw new InvalidOperationException(
                $"Expected the contents of '{ProjectPath}' to be of type '{typeof(T).Name}' but found '{Contents.GetType().Name}'.");

        return typed;
    }
}