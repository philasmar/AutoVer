namespace AutoVer.Services.ProjectFiles;

/// <summary>
/// Resolves the registered <see cref="IProjectFileHandler"/> that owns a given project file path.
/// </summary>
public interface IProjectFileHandlerResolver
{
    /// <summary>
    /// The combined search patterns of every registered handler.
    /// </summary>
    IEnumerable<string> SearchPatterns { get; }

    IProjectFileHandler Resolve(string projectPath);

    /// <summary>
    /// Same as <see cref="Resolve"/> but returns false instead of throwing when no handler
    /// matches. Search patterns are glob-based and can over-match (e.g. backup/editor files
    /// alongside a real project file), so discovery should be able to silently skip those
    /// instead of failing the whole command.
    /// </summary>
    bool TryResolve(string projectPath, out IProjectFileHandler? handler);
}
