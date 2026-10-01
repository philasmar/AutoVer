using System.Text.RegularExpressions;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services.IO;

namespace AutoVer.Services.ProjectFiles;

/// <summary>
/// Reads/writes the version of a Python project: the <c>version</c> key of the
/// <c>[project]</c> table in a <c>pyproject.toml</c> (PEP 621).
/// </summary>
/// <remarks>
/// <para>
/// Explicit-only: <see cref="SearchPatterns"/> is empty, so a pyproject.toml is never
/// auto-discovered and is versioned only when autover.json names it. Many repositories keep a
/// pyproject.toml purely as tool configuration (black, ruff, pytest) with no <c>[project]</c>
/// table; discovering those would start rewriting a lint config in any repository that relies
/// on discovery today.
/// </para>
/// <para>
/// The file is edited line by line, not parsed and re-serialized, so everything except the
/// version value - quote style, spacing, comments, key order, line endings - is left as it was.
/// Values are tokenized well enough (strings with escapes, multi-line strings, nested
/// arrays and inline tables, comments) that a line inside a value is never mistaken for a key
/// or a table header. A version written any way other than a single-line string is refused
/// rather than guessed at.
/// </para>
/// </remarks>
public class PyprojectFileHandler(
    IVersionIncrementer versionIncrementer,
    IFileManager fileManager) : IProjectFileHandler
{
    public const string FileName = "pyproject.toml";

    private const string ProjectTable = "project";

    private const string BareKey = @"[A-Za-z0-9_-]+|""[^""]*""|'[^']*'";

    // A key, including dotted keys (a.b = ...), so their values are tracked like any other.
    private static readonly Regex KeyRegex = new(
        $@"^\s*(?<key>(?:{BareKey})(?:\s*\.\s*(?:{BareKey}))*)\s*=",
        RegexOptions.Compiled);

    // `version = "1.2.3"`, `'version'='1.2.3'  # comment`. Only a single-line basic or literal
    // string followed by nothing but whitespace or a comment is a version AutoVer can rewrite.
    private static readonly Regex VersionValueRegex = new(
        @"^(?<prefix>[^=]*=\s*)(?<open>""(?!"""")|'(?!''))(?<value>[^""'\\\r\n]*)(?<close>\k<open>)(?<suffix>\s*(?:#.*)?)$",
        RegexOptions.Compiled);

    // The Python (PEP 440) versions AutoVer can produce: MAJOR.MINOR.PATCH with an optional
    // pre-release and/or dev segment, in the spellings `packaging` (what pip uses) accepts,
    // e.g. 1.2.3-beta.1 (read as 1.2.3b1) or 1.2.3-rc.1.dev2. Deliberately NOT accepted:
    // post-release forms (1.2.3-post.1, 1.2.3-1), which pip ranks ABOVE 1.2.3 and installs by
    // default - the opposite of what a prerelease label means - and anything pip rejects.
    private static readonly Regex PythonVersionRegex = new(
        @"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-_.]?(?:alpha|a|beta|b|preview|pre|c|rc)[-_.]?[0-9]*)?(?:[-_.]?dev[-_.]?[0-9]*)?\z",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<string> SearchPatterns => [];

    public bool IsMatch(string projectPath) =>
        Path.GetFileName(projectPath).Equals(FileName, StringComparison.OrdinalIgnoreCase);

    public string GetDisplayName(string projectPath) => Path.GetFileName(projectPath);

    public ProjectDefinition Load(string projectPath, string rawContent)
    {
        var lines = rawContent.Split('\n').ToList();
        var layout = Scan(lines, projectPath);

        var version = layout.VersionLine is { } index ? ReadVersion(lines[index], projectPath) : null;

        // Checked here, not when writing: every caller parses a loaded version before the handler
        // is asked to write one, and would otherwise fail with an unexplained parse error.
        if (!string.IsNullOrEmpty(version) && !ThreePartVersion.TryParse(version, out _))
            throw new InvalidProjectException(
                $"'{projectPath}' carries version '{version}', which AutoVer cannot increment: it versions " +
                "MAJOR.MINOR.PATCH, optionally with a '-label' (e.g. 1.2.3 or 1.2.3-beta.1)." + SuggestSpelling(version));

        return new ProjectDefinition(lines, projectPath) { Version = version };
    }

    // A normalized Python spelling (1.2.3b1, 1.2.3.dev4) is the same version with a '-' before its
    // label, which is a form AutoVer can increment - suggest exactly that when it applies.
    private static string SuggestSpelling(string version)
    {
        var match = Regex.Match(version, @"\A([0-9]+\.[0-9]+\.[0-9]+)[._]?([A-Za-z].*)\z");
        if (!match.Success)
            return "";
        var suggestion = $"{match.Groups[1].Value}-{match.Groups[2].Value}";
        return ThreePartVersion.TryParse(suggestion, out _) && PythonVersionRegex.IsMatch(suggestion)
            ? $" '{version}' is the same Python version as '{suggestion}' - write it that way."
            : "";
    }

    public void ValidateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null) =>
        EnsurePythonVersion(ResolveNewVersion(projectDefinition, incrementType, prereleaseLabel, overrideVersion), projectDefinition.ProjectPath);

    public void UpdateVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel = null, string? overrideVersion = null)
    {
        var lines = projectDefinition.GetContents<List<string>>();
        var layout = Scan(lines, projectDefinition.ProjectPath);

        var newVersion = ResolveNewVersion(projectDefinition, incrementType, prereleaseLabel, overrideVersion);
        EnsurePythonVersion(newVersion, projectDefinition.ProjectPath);

        if (layout.VersionLine is { } versionLine)
        {
            var match = VersionValueRegex.Match(lines[versionLine]);
            lines[versionLine] =
                $"{match.Groups["prefix"].Value}{match.Groups["open"].Value}{newVersion}{match.Groups["close"].Value}{match.Groups["suffix"].Value}";
        }
        else
        {
            InsertVersion(lines, layout, newVersion);
        }

        // Keep the in-memory definition in step with what was just written - a version-based tag
        // format is rendered from this after every project has been updated.
        projectDefinition.Version = newVersion;

        fileManager.WriteAllText(projectDefinition.ProjectPath, string.Join('\n', lines));
    }

    private string ResolveNewVersion(ProjectDefinition projectDefinition, IncrementType incrementType, string? prereleaseLabel, string? overrideVersion)
    {
        if (!string.IsNullOrEmpty(overrideVersion))
        {
            if (!ThreePartVersion.TryParse(overrideVersion, out var version))
                throw new InvalidArgumentException($"The version '{overrideVersion}' you are trying to update to is invalid.");
            return version.ToString();
        }

        // Seeded, not incremented - see DockerfileFileHandler. The caller normally supplies the
        // version to seed with; this is only the fallback for a direct call that didn't.
        if (string.IsNullOrEmpty(projectDefinition.Version))
            return versionIncrementer.GetCurrentVersion(null).ToString();

        return versionIncrementer.GetNextVersion(projectDefinition.Version, incrementType, prereleaseLabel).ToString();
    }

    private static void EnsurePythonVersion(string version, string projectPath)
    {
        if (!PythonVersionRegex.IsMatch(version))
            throw new InvalidProjectException(
                $"The version '{version}' cannot be written to '{projectPath}': it is not a Python pre-release or " +
                "release version (PEP 440), so pip would refuse it or misorder it. A prerelease label must be one of " +
                "alpha/a, beta/b, rc/c/pre/preview or dev, optionally followed by a number - e.g. 'beta.1' or 'rc.2'. " +
                "Change the prerelease label: the project's PrereleaseLabel in autover.json, or the version given to --use-version.");
    }

    private sealed record Layout(int HeaderLine, int? VersionLine, int? NameLine, int LastValueLine);

    /// <summary>
    /// Finds the <c>[project]</c> table and, within it, the version and name keys - and rejects
    /// the layouts AutoVer cannot version: no <c>[project]</c> table, a <c>dynamic</c> version,
    /// or a version that is not a plain single-line string.
    /// </summary>
    private static Layout Scan(List<string> lines, string projectPath)
    {
        int? headerLine = null, versionLine = null, nameLine = null;
        var lastValueLine = -1;
        var dynamicVersion = false;
        var hasPoetryTable = false;

        string? table = null;
        var value = new ValueState();
        var collectingDynamic = false;
        var nameValueOpen = false;
        var valueOpenedAt = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // A continuation line of a multi-line value belongs to the key that opened it: it moves
            // the end of [project]'s content (so a seeded version never lands inside the value) but
            // is never a key or a header itself.
            if (value.Open)
            {
                if (table == ProjectTable)
                    lastValueLine = i;
                // A seeded version goes after the END of name's value, never inside it.
                if (nameValueOpen)
                    nameLine = i;
                if (collectingDynamic && MentionsVersion(line, value))
                    dynamicVersion = true;
                value.Advance(line, 0);
                if (!value.Open)
                {
                    collectingDynamic = false;
                    nameValueOpen = false;
                }
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith('['))
            {
                // [[array.of.tables]] is never the [project] table.
                table = trimmed.StartsWith("[[") ? null : NormalizeTableName(trimmed);
                if (table == ProjectTable)
                {
                    if (headerLine is not null)
                        throw new InvalidProjectException($"'{projectPath}' defines the [project] table twice, which is not valid TOML.");
                    headerLine = i;
                    lastValueLine = i;
                }
                if (table == "tool.poetry")
                    hasPoetryTable = true;
                continue;
            }

            var key = KeyRegex.Match(line);
            if (!key.Success)
            {
                // Not a key or a header: still tokenized, so a stray """ or [ is tracked rather
                // than letting the next line be read out of context.
                value.Advance(line, 0);
                if (value.Open)
                    valueOpenedAt = i;
                continue;
            }

            var valueStart = line.IndexOf('=', key.Groups["key"].Index + key.Groups["key"].Length) + 1;
            var isDynamic = table == ProjectTable && KeyName(key) == "dynamic";
            if (isDynamic && MentionsVersion(line[valueStart..], new ValueState()))
                dynamicVersion = true;
            value.Advance(line, valueStart);
            collectingDynamic = isDynamic && value.Open;
            if (value.Open)
                valueOpenedAt = i;

            if (table != ProjectTable)
                continue;

            lastValueLine = i;
            switch (KeyName(key))
            {
                case "version":
                    if (versionLine is not null)
                        throw new InvalidProjectException($"'{projectPath}' sets [project] version twice, which is not valid TOML.");
                    versionLine = i;
                    break;
                case "name":
                    nameLine = i;
                    nameValueOpen = value.Open;
                    break;
            }
        }

        // An unclosed array, inline table or string swallows everything after it; say so, rather
        // than report whatever the swallowed part happened to contain (or lack).
        if (value.Open)
            throw new InvalidProjectException(
                $"'{projectPath}' is not valid TOML: the value starting on line {valueOpenedAt + 1} is never closed.");

        if (headerLine is null)
            throw new InvalidProjectException(
                $"'{projectPath}' has no [project] table, so there is no version for AutoVer to manage. " +
                (hasPoetryTable
                    ? "Poetry's [tool.poetry] version is not supported: move the metadata to the standard [project] table, which Poetry 2 reads."
                    : "Add a [project] table with name and version (PEP 621)."));

        if (dynamicVersion)
            throw new InvalidProjectException(
                $"'{projectPath}' lists \"version\" in [project] dynamic, so the build backend computes the version " +
                "(from git tags, a file, ...) and there is nothing for AutoVer to write. Remove \"version\" from dynamic " +
                "and add version = \"x.y.z\" to [project], or let AutoVer take the version from tags instead (VersionFromTag).");

        return new Layout(headerLine.Value, versionLine, nameLine, lastValueLine);
    }

    // The key's name when it is a single segment; a dotted key (version.x) is never "version".
    private static string? KeyName(Match key)
    {
        var text = key.Groups["key"].Value.Trim();
        char? quote = null;
        foreach (var c in text)
        {
            if (quote is null && c == '.') return null;
            if (c is '"' or '\'') quote = quote == c ? null : quote ?? c;
        }
        return text.Trim('"', '\'');
    }

    private static string ReadVersion(string line, string projectPath)
    {
        var match = VersionValueRegex.Match(line.TrimEnd('\r'));
        if (!match.Success)
        {
            var rest = line[(line.IndexOf('=') + 1)..].Trim();
            throw new InvalidProjectException(
                $"'{projectPath}' sets [project] version to {rest}, which is not a plain single-line string. " +
                "AutoVer can only manage a version written as version = \"x.y.z\".");
        }
        return match.Groups["value"].Value;
    }

    /// <summary>
    /// Adds <c>version = "..."</c> to a [project] table that has none: right after <c>name</c>,
    /// where PEP 621 examples and most tools put it, else after the end of the table's last value.
    /// </summary>
    private static void InsertVersion(List<string> lines, Layout layout, string version)
    {
        var line = $"version = \"{version}\"";
        // Lines keep their own trailing '\r'; match the file rather than mixing endings.
        var crlf = lines.Any(existing => existing.EndsWith('\r'));
        var after = layout.NameLine ?? layout.LastValueLine;

        if (after + 1 < lines.Count)
        {
            lines.Insert(after + 1, crlf ? line + '\r' : line);
            return;
        }

        // Inserting after the last line of a file with no trailing newline: the previous line gains
        // the terminator it lacked and the new last line has none, as the file had.
        if (crlf)
            lines[after] += '\r';
        lines.Add(line);
    }

    // Whether this stretch of a `dynamic` array names "version" as an element (not in a comment).
    private static bool MentionsVersion(string text, ValueState state)
    {
        var code = state.CodeOnly(text);
        return Regex.IsMatch(code, @"(?:""version""|'version')");
    }

    // `[ "project" ]  # comment` -> project. Whitespace around the name and quoted segments are
    // both valid TOML for the same table.
    private static string NormalizeTableName(string header)
    {
        var end = header.IndexOf(']');
        var inner = end > 0 ? header[1..end] : header[1..];
        var segments = inner.Split('.').Select(segment => segment.Trim().Trim('"', '\''));
        return string.Join('.', segments);
    }

    /// <summary>
    /// Tracks, across lines, whether a TOML value is still open: an unclosed multi-line string, or
    /// unbalanced <c>[ ]</c> / <c>{ }</c> - counted outside strings and comments only, so the
    /// <c>]</c> in <c>"requests[socks]&gt;=2"</c> does not close an array.
    /// </summary>
    private sealed class ValueState
    {
        private int _depth;
        private string? _multiline;

        public bool Open => _depth > 0 || _multiline is not null;

        public void Advance(string line, int start) => Walk(line, start, collect: null);

        // The part of the line outside strings is irrelevant to MentionsVersion; the part inside
        // them is what it looks at. Returns the text with comments removed.
        public string CodeOnly(string line)
        {
            var copy = new ValueState { _depth = _depth, _multiline = _multiline };
            var kept = new System.Text.StringBuilder();
            copy.Walk(line, 0, kept);
            return kept.ToString();
        }

        private void Walk(string line, int start, System.Text.StringBuilder? collect)
        {
            var i = start;
            while (i < line.Length)
            {
                if (_multiline is not null)
                {
                    // Basic multi-line strings honour escapes: \""" is an escaped quote followed by
                    // two quotes, not the end. A closing run may be 3-5 quotes; the last 3 close it.
                    var quote = _multiline[0];
                    if (quote == '"' && line[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (line[i] == quote && string.CompareOrdinal(line, i, _multiline, 0, 3) == 0)
                    {
                        var run = 0;
                        while (i + run < line.Length && line[i + run] == quote && run < 5)
                            run++;
                        i += run;
                        _multiline = null;
                        continue;
                    }
                    i++;
                    continue;
                }

                var c = line[i];
                if (c == '#')
                    return;

                if (c is '"' or '\'')
                {
                    var triple = new string(c, 3);
                    if (string.CompareOrdinal(line, i, triple, 0, 3) == 0)
                    {
                        _multiline = triple;
                        i += 3;
                        continue;
                    }

                    var end = i + 1;
                    while (end < line.Length && line[end] != c)
                        end += c == '"' && line[end] == '\\' ? 2 : 1;
                    collect?.Append(line, i, Math.Min(end + 1, line.Length) - i);
                    i = end + 1;
                    continue;
                }

                if (c is '[' or '{') _depth++;
                else if (c is ']' or '}') _depth = Math.Max(0, _depth - 1);
                collect?.Append(c);
                i++;
            }
        }
    }
}
