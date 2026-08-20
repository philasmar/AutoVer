using System.Text.RegularExpressions;
using AutoVer.Constants;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services.ProjectFiles;

/// <summary>
/// Reads/writes the version stored in a Dockerfile's "org.opencontainers.image.version" LABEL.
/// </summary>
public class DockerfileFileHandler(
    IVersionIncrementer versionIncrementer,
    IFileManager fileManager) : IProjectFileHandler
{
    private static readonly Regex VersionLabelRegex = new(
        $@"(?<prefix>{Regex.Escape(ProjectConstants.DockerImageVersionLabel)}=""?)(?<version>[^""\s]*)(?<suffix>""?)",
        RegexOptions.Compiled);

    // Common backup/editor/VCS artifact suffixes that must never be treated as a real
    // Dockerfile, even though they'd otherwise match the "Dockerfile.*" naming convention.
    // This is a heuristic, not an exhaustive guarantee — naming conventions for "environment
    // variant" (Dockerfile.prod) and "junk file" (Dockerfile.old) are not reliably
    // distinguishable by suffix alone. Users who hit an uncommon junk-file convention can
    // work around it by listing their real projects explicitly in autover.json instead of
    // relying on auto-discovery.
    private static readonly string[] IgnoredSuffixes = [".bak", ".orig", ".swp", ".swo", ".tmp", "~"];

    public IEnumerable<string> SearchPatterns => new[] { "Dockerfile", "Dockerfile.*", "*.Dockerfile" };

    public bool IsMatch(string projectPath)
    {
        var fileName = Path.GetFileName(projectPath);
        if (IgnoredSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return false;

        return fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase);
    }

    public string GetDisplayName(string projectPath) => Path.GetFileName(projectPath);

    public ProjectDefinition Load(string projectPath, string rawContent)
    {
        var lines = SplitLines(rawContent);
        var projectDefinition = new ProjectDefinition(lines, projectPath);

        var lineIndexes = FindVersionLabelLines(lines);
        if (lineIndexes.Count > 0)
        {
            projectDefinition.Version = VersionLabelRegex.Match(lines[lineIndexes[0]]).Groups["version"].Value;
        }

        return projectDefinition;
    }

    public void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null)
    {
        var lines = projectDefinition.GetContents<List<string>>();
        var lineIndexes = FindVersionLabelLines(lines);
        if (lineIndexes.Count == 0)
            throw new NoVersionTagException($"The Dockerfile '{projectDefinition.ProjectPath}' does not have a '{ProjectConstants.DockerImageVersionLabel}' LABEL. Add one and run the tool again.");

        string newVersion;
        if (string.IsNullOrEmpty(overrideVersion))
        {
            var currentVersion = VersionLabelRegex.Match(lines[lineIndexes[0]]).Groups["version"].Value;
            newVersion = versionIncrementer.GetNextVersion(currentVersion, incrementType, prereleaseLabel).ToString();
        }
        else
        {
            if (!ThreePartVersion.TryParse(overrideVersion, out var version))
                throw new InvalidArgumentException($"The version '{overrideVersion}' you are trying to update to is invalid.");
            newVersion = version.ToString();
        }

        // Multi-stage Dockerfiles can repeat the same version LABEL per stage; keep every
        // occurrence in sync rather than only updating the first one found.
        foreach (var lineIndex in lineIndexes)
        {
            lines[lineIndex] = VersionLabelRegex.Replace(lines[lineIndex], match =>
                $"{match.Groups["prefix"].Value}{newVersion}{match.Groups["suffix"].Value}");
        }

        fileManager.WriteAllText(projectDefinition.ProjectPath, string.Join('\n', lines));
    }

    // Walks the file tracking whether each line belongs to a LABEL instruction, including
    // any of its backslash-continuation lines (e.g. "LABEL a=\"1\" \" followed by a line
    // holding the actual version key=value pair), and collects every line within one that
    // matches the version label.
    private static List<int> FindVersionLabelLines(List<string> lines)
    {
        var lineIndexes = new List<int>();
        var insideLabelInstruction = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var isPartOfLabelInstruction = insideLabelInstruction || IsLabelInstruction(line);

            if (isPartOfLabelInstruction && VersionLabelRegex.IsMatch(line))
                lineIndexes.Add(i);

            insideLabelInstruction = isPartOfLabelInstruction && EndsWithLineContinuation(line);
        }

        return lineIndexes;
    }

    // Only treats the line as a version source if it's an actual LABEL instruction, not a
    // comment or other line that happens to mention the same label text.
    private static bool IsLabelInstruction(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > "LABEL".Length
            && trimmed.AsSpan(0, "LABEL".Length).Equals("LABEL", StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(trimmed["LABEL".Length]);
    }

    private static bool EndsWithLineContinuation(string line) => line.TrimEnd('\r').TrimEnd().EndsWith('\\');

    // Splits on '\n' only, keeping each line's own trailing '\r' (if any) intact, so
    // rejoining with '\n' reproduces the file's original line endings instead of forcing
    // every line to LF.
    private static List<string> SplitLines(string content) => content.Split('\n').ToList();
}
