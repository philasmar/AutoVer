using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.Services;

public class VersionHandler(
    IGitHandler gitHandler,
    IConfigurationManager configurationManager,
    IToolInteractiveService toolInteractiveService) : IVersionHandler
{
    private readonly DateTime _releaseDate = DateTime.UtcNow;
    private readonly Dictionary<string, List<VersionTag>> _versionTagsCache = new();
    private readonly Dictionary<string, VersionTagFormat> _formatCache = new();
    private readonly Dictionary<string, VersionTag> _currentTagCache = new();

    public string GetNewVersionTag(UserConfiguration configuration)
    {
        var format = GetTagFormat(configuration);
        var version = GetReleaseVersion(configuration);
        var iteration = GetNextIteration(configuration, format, version);

        // Only a tag has to be unique, so this is enforced here rather than inside
        // GetNextIteration: a release name that repeats the previous one is untidy at worst, and a
        // run that isn't going to create a tag at all (--no-tag) shouldn't be blocked by it.
        if (iteration > 1 && !format.SupportsIteration)
            throw new InvalidVersionTagException(
                $"The tag '{format.Render(version, _releaseDate, 1)}' already exists and the format '{format.Format}' " +
                "has no {iteration} placeholder to distinguish a repeat release. Either increment the version " +
                "before releasing again, or add an optional iteration group to 'TagFormat' (e.g. " +
                $"'{format.Format}[-{{iteration}}]').");

        var tagName = format.Render(version, _releaseDate, iteration);

        // Checked against git's own grammar rather than a hand-maintained copy of it, and against
        // the rendered name rather than the format, so that an illegal character arriving through a
        // placeholder value (a prerelease label, say) is caught too. VersionCommand resolves the tag
        // name before it commits, so this fails while the repository is still untouched.
        if (!gitHandler.IsValidTagName(tagName))
            throw new InvalidVersionTagException(
                $"'{nameof(UserConfiguration.TagFormat)}' produced '{tagName}', which git will not accept as a tag " +
                $"name. Check '{format.Format}' - a tag name cannot contain a space, '~', '^', ':', '?', '*', '[', " +
                "'\\', '..' or '@{', cannot end with '.' or '.lock', and cannot have an empty path segment.");

        return tagName;
    }

    public string GetNewReleaseName(UserConfiguration configuration)
    {
        var tagFormat = GetTagFormat(configuration);
        var version = GetReleaseVersion(configuration);

        // The iteration is a property of the release itself, so it's derived from the tag format
        // (the tag is what identifies a release) and then reused here. Deriving it from the
        // release-name format instead would let a name drift out of step with its own tag.
        return GetReleaseNameFormat(configuration)
            .Render(version, _releaseDate, GetNextIteration(configuration, tagFormat, version));
    }

    public string GetCurrentVersionTag(string projectPath)
    {
        var gitRoot = gitHandler.FindGitRootDirectory(projectPath);

        // Reached before a full UserConfiguration exists: the changelog needs the current tag in
        // order to load the configuration as it was *at* that tag, but finding the tag needs the
        // tag format. Read just the repository-level settings from disk to break the cycle.
        var settings = configurationManager.LoadRepositorySettings(gitRoot);
        var format = GetFormat(
            settings?.EffectiveTagFormat ?? UserConfiguration.DefaultTagFormat,
            nameof(UserConfiguration.TagFormat));

        return GetCurrentTag(gitRoot, format).Raw;
    }

    public string GetCurrentVersionTag(UserConfiguration configuration) =>
        GetCurrentTag(configuration.GitRoot, GetTagFormat(configuration)).Raw;

    public string GetCurrentReleaseName(UserConfiguration configuration)
    {
        var current = GetCurrentTag(configuration.GitRoot, GetTagFormat(configuration));

        // Configuration validation keeps both formats in the same family, so a date-based name
        // can only be asked for when the tag it describes actually carries a date.
        return GetReleaseNameFormat(configuration)
            .Render(current.Version, current.Date ?? _releaseDate, current.Iteration);
    }

    public string? GetLastVersionTag(UserConfiguration configuration)
    {
        var format = GetTagFormat(configuration);
        var versionTags = GetVersionTags(configuration.GitRoot, format);

        if (versionTags.Count == 0)
            throw new InvalidVersionTagException(
                $"The Git repository '{configuration.GitRoot}' does not have a valid version tag. Please run 'autover version' first.");

        // The release before the current one, which isn't necessarily the second-newest overall:
        // when the current release is a backport, the releases ordering above it belong to a newer
        // line and the range has to start from the one below it instead.
        var current = GetCurrentTag(configuration.GitRoot, format);
        var currentIndex = versionTags.FindIndex(tag => tag.Raw.Equals(current.Raw, StringComparison.Ordinal));

        return currentIndex >= 0 && currentIndex + 1 < versionTags.Count
            ? versionTags[currentIndex + 1].Raw
            : null;
    }

    public ThreePartVersion? GetCurrentTagVersion(UserConfiguration configuration)
    {
        var format = GetTagFormat(configuration);

        // No release yet is an ordinary state here, not an error: it is how the first release of a
        // tag-sourced repository is recognised.
        return GetVersionTags(configuration.GitRoot, format).Count == 0
            ? null
            : GetCurrentTag(configuration.GitRoot, format).Version;
    }

    private VersionTag GetCurrentTag(string gitRoot, VersionTagFormat format)
    {
        // Generating a changelog asks for the current release several times over - its name, its
        // tag, and the release before it - and resolving it walks history through a freshly opened
        // repository each time. Cached because the answer can't change mid-command (a changelog
        // commits only once every read is done), and because keeping the number of concurrently
        // opened libgit2 repositories down matters: it races on its own lazy initialization, which
        // is why the git-touching tests are serialized and retried.
        var cacheKey = $"{gitRoot}\n{format.Format}";
        if (_currentTagCache.TryGetValue(cacheKey, out var cachedCurrentTag))
            return cachedCurrentTag;

        var versionTags = GetVersionTags(gitRoot, format);

        if (versionTags.Count == 0)
            throw new InvalidVersionTagException(
                $"The Git repository '{gitRoot}' does not have a valid version tag. Please run 'autover version' first.");

        // The nearest release reachable from HEAD is the one the current working state belongs to.
        // Highest-ordering is only an inference, and version ordering makes it the wrong one after a
        // backport: with 2.0.0 released and 1.9.1 then cut from the older line, the highest tag isn't
        // the release being described, and a changelog built for it covers the wrong range. Nearest
        // rather than "on HEAD" because `autover changelog` commits the changelog itself, moving HEAD
        // off the tagged commit before the release name and tag are read back. Date-based tags never
        // showed any of this, since newest-by-date was always the release just cut.
        var nearest = gitHandler
            .FindNearestReachableTags(gitRoot, versionTags.Select(tag => tag.Raw).ToList())
            .ToHashSet(StringComparer.Ordinal);

        // versionTags is ordered newest-first, so this takes the highest of that commit's tags.
        var currentTag = versionTags.FirstOrDefault(tag => nearest.Contains(tag.Raw)) ?? versionTags[0];

        _currentTagCache[cacheKey] = currentTag;
        return currentTag;
    }

    /// <summary>
    /// How many releases of this same version/date have already been tagged, plus one. 1 for the
    /// first, which is the value at which an optional <c>[{iteration}]</c> group renders as nothing.
    /// </summary>
    private int GetNextIteration(UserConfiguration configuration, VersionTagFormat format, ThreePartVersion? version)
    {
        // Whether an existing tag belongs to the release being cut is decided by the format itself:
        // re-render at that tag's iteration and compare the result to the tag verbatim. Comparing
        // parsed components instead would be lossy - anything the format doesn't render is simply
        // absent from the tag, so e.g. a format without {prerelease} renders both 1.0.1-beta and
        // 1.0.1-rc as "v1.0.1". Those are different versions but the same tag, and treating them as
        // distinct releases would render a name that already exists.
        var iteration = 1;

        foreach (var tag in GetVersionTags(configuration.GitRoot, format))
        {
            if (string.Equals(format.Render(version, _releaseDate, tag.Iteration), tag.Raw, StringComparison.Ordinal))
                iteration = Math.Max(iteration, tag.Iteration + 1);
        }

        return iteration;
    }

    /// <summary>
    /// The version a version-based tag is built from. Validation rejects the configurations in which
    /// several projects could disagree here, so taking the highest is a safety net rather than a
    /// policy - it only ever sees projects that were just set to the same version.
    /// </summary>
    private static ThreePartVersion? GetReleaseVersion(UserConfiguration configuration)
    {
        // Set when the version came from the repository's tags rather than a project file, in which
        // case there is no file to read it back from.
        if (configuration.ResolvedReleaseVersion is not null)
            return configuration.ResolvedReleaseVersion;

        ThreePartVersion? highest = null;

        foreach (var project in configuration.Projects.SelectMany(container => container.Projects))
        {
            if (!ThreePartVersion.TryParse(project.ProjectDefinition.Version, out var version))
                continue;

            if (highest is null || version > highest)
                highest = version;
        }

        return highest;
    }

    private List<VersionTag> GetVersionTags(string gitRoot, VersionTagFormat format)
    {
        var cacheKey = $"{gitRoot}\n{format.Format}";
        if (_versionTagsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var versionTags = new List<VersionTag>();
        var skipped = 0;

        foreach (var tag in gitHandler.GetTags(gitRoot))
        {
            if (format.TryParseTag(tag, out var parsed))
                versionTags.Add(parsed!);
            else
                skipped++;
        }

        versionTags.Sort((left, right) => right.CompareTo(left));

        // A format change orphans every tag written under the old one, and AutoVer uses tag history
        // to decide which commits a changelog covers - so silently seeing zero tags would quietly
        // produce a changelog for the wrong range. Only worth saying when nothing matched at all:
        // a repository that has matching tags alongside unrelated ones (a hand-made tag, a tag from
        // before it adopted AutoVer) is working exactly as intended and shouldn't be nagged on every
        // run. Goes to stderr because stdout carries values meant for shell capture, e.g.
        // TAG=$(autover changelog --tag-name).
        if (skipped > 0 && versionTags.Count == 0)
            toolInteractiveService.WriteErrorLine(
                $"Warning: ignored {skipped} tag(s) that don't match the configured tag format '{format.Format}', " +
                "and found no release history. This release will be treated as the repository's first.");

        _versionTagsCache[cacheKey] = versionTags;
        return versionTags;
    }

    private VersionTagFormat GetTagFormat(UserConfiguration configuration) =>
        GetFormat(configuration.EffectiveTagFormat, nameof(UserConfiguration.TagFormat));

    private VersionTagFormat GetReleaseNameFormat(UserConfiguration configuration) =>
        GetFormat(
            configuration.ResolveReleaseNameFormat(GetTagFormat(configuration).Family),
            nameof(UserConfiguration.ReleaseNameFormat));

    private VersionTagFormat GetFormat(string format, string settingName)
    {
        var cacheKey = $"{settingName}\n{format}";
        if (_formatCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var parsed = VersionTagFormat.Parse(format, settingName);
        _formatCache[cacheKey] = parsed;
        return parsed;
    }
}
