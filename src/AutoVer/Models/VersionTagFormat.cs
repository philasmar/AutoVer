using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoVer.Exceptions;

namespace AutoVer.Models;

/// <summary>
/// Which ordering key a tag format carries. A tag isn't only written by AutoVer - it's read
/// back to work out which release was the most recent (and, from that, the commit range a
/// changelog covers), so every format has to yield an unambiguous sort order. Semver and date
/// orderings disagree the moment a release isn't strictly linear (a backport, or an explicit
/// --use-version), which is why a single format may carry one family or the other, never both.
/// </summary>
public enum TagFormatFamily
{
    Semver,
    Date
}

internal enum TagPlaceholder
{
    Major,
    Minor,
    Patch,
    Prerelease,
    Year,
    Month,
    Day,
    Date,
    Iteration
}

/// <summary>
/// A parsed, validated tag (or release name) format such as
/// <c>v{major}.{minor}.{patch}[-{prerelease}]</c> or <c>release_{date}[_{iteration}]</c>.
///
/// Text outside braces is literal. <c>{{</c>, <c>}}</c>, <c>[[</c> and <c>]]</c> are escapes for
/// the corresponding literal character. A <c>[...]</c> group renders only when every placeholder
/// inside it has a value - which is what lets one format string cover both
/// <c>release_2026-09-02</c> and <c>release_2026-09-02_5</c>, or both <c>v1.4.0</c> and
/// <c>v1.4.0-beta.1</c>, instead of needing a separate format per case.
/// </summary>
public sealed class VersionTagFormat
{
    // Only these two carry an "absent" state worth eliding, so only these two are meaningful
    // inside an optional group: a prerelease label can be unset, and an iteration is implicitly
    // 1 for the first release of a given version/date. Major/minor/patch/date components always
    // have a value, so wrapping one in [...] would be silently pointless.
    private static readonly TagPlaceholder[] OptionalCapablePlaceholders =
        [TagPlaceholder.Prerelease, TagPlaceholder.Iteration];

    private static readonly TagPlaceholder[] SemverPlaceholders =
        [TagPlaceholder.Major, TagPlaceholder.Minor, TagPlaceholder.Patch, TagPlaceholder.Prerelease];

    private static readonly TagPlaceholder[] DatePlaceholders =
        [TagPlaceholder.Year, TagPlaceholder.Month, TagPlaceholder.Day, TagPlaceholder.Date];

    private static readonly Dictionary<string, TagPlaceholder> PlaceholderNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["major"] = TagPlaceholder.Major,
            ["minor"] = TagPlaceholder.Minor,
            ["patch"] = TagPlaceholder.Patch,
            ["prerelease"] = TagPlaceholder.Prerelease,
            ["year"] = TagPlaceholder.Year,
            ["month"] = TagPlaceholder.Month,
            ["day"] = TagPlaceholder.Day,
            ["date"] = TagPlaceholder.Date,
            ["iteration"] = TagPlaceholder.Iteration
        };

    private static readonly Dictionary<TagPlaceholder, string> DefaultDateFormats =
        new()
        {
            [TagPlaceholder.Year] = "yyyy",
            [TagPlaceholder.Month] = "MM",
            [TagPlaceholder.Day] = "dd",
            [TagPlaceholder.Date] = "yyyy-MM-dd"
        };

    private abstract record Segment;

    private sealed record LiteralSegment(string Text) : Segment;

    private sealed record PlaceholderSegment(TagPlaceholder Kind, string? DateFormat) : Segment;

    private sealed record OptionalSegment(List<Segment> Children) : Segment;

    private readonly List<Segment> _segments;
    private readonly Regex _matcher;

    /// <summary>
    /// The date placeholders in the order they appear, paired with the .NET format each one
    /// renders with. Concatenating the captured text of all of them - and, separately, their
    /// formats - reconstitutes one composite value/format pair that DateTime.ParseExact can
    /// invert, which is how a date survives a round-trip through an arbitrary layout.
    /// </summary>
    private readonly List<(string GroupName, string DateFormat)> _dateGroups;

    private readonly HashSet<TagPlaceholder> _kinds;

    // Deliberately multi-digit and distinctive: values like 1.1.1 would round-trip even through an
    // ambiguous format, hiding exactly the defect the self-check exists to find. A single-digit month
    // beside a two-digit day exposes the same ambiguity in variable-width date specifiers.
    private static readonly ThreePartVersion ProbeVersion =
        new() { Major = 1, Minor = 23, Patch = 456, PrereleaseLabel = "rc.7" };

    private static readonly DateTime ProbeDate = new(2026, 1, 12);

    private const int ProbeIteration = 89;

    /// <summary>
    /// A two-digit year is only invertible inside a fixed 100-year window, and the invariant
    /// culture's ends at 2049 - so a 2050 release using {year:yy} would render "50" and read back as
    /// 1950, sorting below every existing release and taking the changelog range with it. Pinning
    /// the window to 2000-2099 keeps two-digit years invertible for as long as they're plausible.
    /// </summary>
    private static readonly CultureInfo DateCulture = CreateDateCulture();

    private static CultureInfo CreateDateCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2099;
        return culture;
    }

    public string Format { get; }

    public TagFormatFamily Family { get; }

    /// <summary>
    /// Whether the format can represent a second-or-later release of the same version/date. If it
    /// can't, a collision has nowhere to go and is reported as an error rather than silently
    /// producing a duplicate tag.
    /// </summary>
    public bool SupportsIteration { get; }

    private VersionTagFormat(
        string format,
        List<Segment> segments,
        TagFormatFamily family,
        bool supportsIteration,
        Regex matcher,
        List<(string GroupName, string DateFormat)> dateGroups,
        HashSet<TagPlaceholder> kinds)
    {
        _kinds = kinds;
        Format = format;
        _segments = segments;
        Family = family;
        SupportsIteration = supportsIteration;
        _matcher = matcher;
        _dateGroups = dateGroups;
    }

    public static VersionTagFormat Parse(string format, string settingName)
    {
        if (string.IsNullOrWhiteSpace(format))
            throw new InvalidUserConfigurationException($"'{settingName}' cannot be empty.");

        var segments = Tokenize(format, settingName);
        var placeholders = Flatten(segments)
            .OfType<PlaceholderSegment>()
            .ToList();

        var duplicate = placeholders
            .GroupBy(placeholder => placeholder.Kind)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidUserConfigurationException(
                $"'{settingName}' uses the '{{{duplicate.Key.ToString().ToLowerInvariant()}}}' placeholder more than once in '{format}'. " +
                "Each placeholder may appear at most once, so that a tag can be read back unambiguously.");

        var kinds = placeholders.Select(placeholder => placeholder.Kind).ToHashSet();
        var hasSemver = SemverPlaceholders.Any(kinds.Contains);
        var hasDate = DatePlaceholders.Any(kinds.Contains);

        if (hasSemver && hasDate)
            throw new InvalidUserConfigurationException(
                $"'{settingName}' mixes version and date placeholders in '{format}'. A format may use " +
                "{major}/{minor}/{patch}/{prerelease} or {year}/{month}/{day}/{date}, but not both - the two " +
                "sort orders disagree whenever a release isn't strictly linear (e.g. a backport), leaving no " +
                "single correct answer for which release was the most recent.");

        if (!hasSemver && !hasDate)
            throw new InvalidUserConfigurationException(
                $"'{settingName}' has no version or date placeholder in '{format}'. Add either a full " +
                "{major}.{minor}.{patch} or a date ({date}, or all of {year}/{month}/{day}) so releases can be ordered.");

        TagFormatFamily family;
        if (hasSemver)
        {
            var missing = new[] { TagPlaceholder.Major, TagPlaceholder.Minor, TagPlaceholder.Patch }
                .Where(kind => !kinds.Contains(kind))
                .Select(kind => $"{{{kind.ToString().ToLowerInvariant()}}}")
                .ToList();
            if (missing.Count > 0)
                throw new InvalidUserConfigurationException(
                    $"'{settingName}' is missing {string.Join(", ", missing)} in '{format}'. A version-based format " +
                    "needs all of {major}, {minor} and {patch} - a partial version would collide on every release " +
                    "that only changes an omitted component.");

            family = TagFormatFamily.Semver;
        }
        else
        {
            // {date} is the shorthand for a whole date, so pairing it with individual components
            // would build a composite parse format with the same specifier repeated (e.g.
            // "yyyy-MM-dd" + "yyyy"), which renders fine but can't be read back - the tag would
            // silently drop out of release history.
            var components = new[] { TagPlaceholder.Year, TagPlaceholder.Month, TagPlaceholder.Day }
                .Where(kinds.Contains)
                .Select(kind => $"{{{kind.ToString().ToLowerInvariant()}}}")
                .ToList();
            if (kinds.Contains(TagPlaceholder.Date) && components.Count > 0)
                throw new InvalidUserConfigurationException(
                    $"'{settingName}' combines {{date}} with {string.Join(", ", components)} in '{format}'. " +
                    "{date} already covers the whole date - use either {date} on its own (optionally with a " +
                    "format, e.g. {date:yyyyMMdd}) or the individual components.");

            if (!kinds.Contains(TagPlaceholder.Date) &&
                !(kinds.Contains(TagPlaceholder.Year) && kinds.Contains(TagPlaceholder.Month) && kinds.Contains(TagPlaceholder.Day)))
                throw new InvalidUserConfigurationException(
                    $"'{settingName}' has an incomplete date in '{format}'. Use {{date}}, or all of {{year}}, " +
                    "{month} and {day} - a partial date can't order releases.");

            family = TagFormatFamily.Date;
        }

        var dateGroups = placeholders
            .Where(placeholder => DatePlaceholders.Contains(placeholder.Kind))
            .Select(placeholder => (
                GroupName: GroupName(placeholder.Kind),
                DateFormat: placeholder.DateFormat ?? DefaultDateFormats[placeholder.Kind]))
            .ToList();

        if (family == TagFormatFamily.Date)
        {
            // Guards against a format that looks complete but resolves to less than day
            // granularity - {date:yyyy} satisfies "has {date}" while still collapsing every
            // release in a year onto one tag.
            var composite = string.Concat(dateGroups.Select(group => group.DateFormat));
            var missingParts = new List<string>();
            if (!composite.Contains('y')) missingParts.Add("year");
            if (!composite.Contains('M')) missingParts.Add("month");
            if (!composite.Contains('d')) missingParts.Add("day");
            if (missingParts.Count > 0)
                throw new InvalidUserConfigurationException(
                    $"'{settingName}' resolves to a date with no {string.Join(" or ", missingParts)} component in " +
                    $"'{format}'. The date placeholders must together specify a year, month and day.");
        }

        var matcher = new Regex(BuildPattern(segments), RegexOptions.CultureInvariant);

        var tagFormat = new VersionTagFormat(
            format,
            segments,
            family,
            kinds.Contains(TagPlaceholder.Iteration),
            matcher,
            dateGroups,
            kinds);

        tagFormat.EnsureRoundTrips(settingName);

        return tagFormat;
    }

    /// <summary>
    /// Proves the format is invertible: what it renders has to read back as the same components.
    /// Checked by construction rather than by enumerating the ways a format can be ambiguous, since
    /// that list is easy to get wrong - adjacent variable-width placeholders are one case
    /// (<c>{major}{minor}{patch}</c> renders 1.23.456 as "123456", which reads back as 1234.5.6),
    /// and a format that can't be read back silently drops its own tags out of release history.
    /// </summary>
    private void EnsureRoundTrips(string settingName)
    {
        var probe = Render(ProbeVersion, ProbeDate, ProbeIteration);

        string? problem = null;

        if (!TryParseTag(probe, out var readBack))
        {
            problem = "cannot be read back at all";
        }
        else if (_kinds.Contains(TagPlaceholder.Major) &&
                 (readBack!.Version?.Major != ProbeVersion.Major ||
                  readBack.Version?.Minor != ProbeVersion.Minor ||
                  readBack.Version?.Patch != ProbeVersion.Patch))
        {
            problem = $"reads back as version {readBack!.Version?.Major}.{readBack.Version?.Minor}.{readBack.Version?.Patch} " +
                      $"instead of {ProbeVersion.Major}.{ProbeVersion.Minor}.{ProbeVersion.Patch}";
        }
        else if (_kinds.Contains(TagPlaceholder.Prerelease) &&
                 readBack!.Version?.PrereleaseLabel != ProbeVersion.PrereleaseLabel)
        {
            problem = $"reads back the prerelease as '{readBack!.Version?.PrereleaseLabel}' instead of '{ProbeVersion.PrereleaseLabel}'";
        }
        else if (Family == TagFormatFamily.Date && readBack!.Date != ProbeDate)
        {
            problem = $"reads back as the date {readBack!.Date:yyyy-MM-dd} instead of {ProbeDate:yyyy-MM-dd}";
        }
        else if (_kinds.Contains(TagPlaceholder.Iteration) && readBack!.Iteration != ProbeIteration)
        {
            problem = $"reads back the iteration as {readBack!.Iteration} instead of {ProbeIteration}";
        }

        if (problem is not null)
            throw new InvalidUserConfigurationException(
                $"'{settingName}' ('{Format}') is ambiguous: it renders '{probe}', which {problem}. " +
                "Separate adjacent placeholders with literal text (e.g. '.' or '-'), or use fixed-width " +
                "date specifiers, so a rendered value can be read back unchanged.");
    }

    /// <summary>
    /// Renders the tag for a release. <paramref name="iteration"/> is 1 for the first release of a
    /// given version/date and increments only to break a collision.
    /// </summary>
    public string Render(ThreePartVersion? version, DateTime date, int iteration)
    {
        if (Family == TagFormatFamily.Semver && version is null)
            throw new InvalidVersionTagException(
                $"The format '{Format}' needs a project version to render, but none was available.");

        var builder = new StringBuilder();
        RenderSegments(_segments, version, date, iteration, builder);
        return builder.ToString();
    }

    /// <summary>
    /// The inverse of <see cref="Render"/> - recovers a tag's ordering components, or returns false
    /// for any tag this format didn't produce (which is how tags left over from a previous format,
    /// or unrelated tags in the repo, get excluded from release history).
    /// </summary>
    public bool TryParseTag(string tag, out VersionTag? parsed)
    {
        parsed = null;

        var match = _matcher.Match(tag);
        if (!match.Success)
            return false;

        var iteration = 1;
        var iterationGroup = match.Groups[GroupName(TagPlaceholder.Iteration)];
        if (iterationGroup.Success && !int.TryParse(iterationGroup.Value, out iteration))
            return false;

        ThreePartVersion? version = null;
        DateTime? date = null;

        if (Family == TagFormatFamily.Semver)
        {
            if (!int.TryParse(match.Groups[GroupName(TagPlaceholder.Major)].Value, out var major) ||
                !int.TryParse(match.Groups[GroupName(TagPlaceholder.Minor)].Value, out var minor) ||
                !int.TryParse(match.Groups[GroupName(TagPlaceholder.Patch)].Value, out var patch))
                return false;

            var prereleaseGroup = match.Groups[GroupName(TagPlaceholder.Prerelease)];

            version = new ThreePartVersion
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                PrereleaseLabel = prereleaseGroup.Success && !string.IsNullOrEmpty(prereleaseGroup.Value)
                    ? prereleaseGroup.Value
                    : null
            };
        }
        else
        {
            var compositeFormat = string.Concat(_dateGroups.Select(group => group.DateFormat));
            var compositeValue = string.Concat(_dateGroups.Select(group => match.Groups[group.GroupName].Value));

            if (!DateTime.TryParseExact(
                    compositeValue,
                    compositeFormat,
                    DateCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
                return false;

            date = parsedDate;
        }

        parsed = new VersionTag(tag, Family, version, date, iteration);
        return true;
    }

    private void RenderSegments(
        IEnumerable<Segment> segments,
        ThreePartVersion? version,
        DateTime date,
        int iteration,
        StringBuilder builder)
    {
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case LiteralSegment literal:
                    builder.Append(literal.Text);
                    break;

                case PlaceholderSegment placeholder:
                    builder.Append(RenderPlaceholder(placeholder, version, date, iteration));
                    break;

                case OptionalSegment optional:
                    if (Flatten(optional.Children)
                        .OfType<PlaceholderSegment>()
                        .All(child => HasValue(child.Kind, version, iteration)))
                        RenderSegments(optional.Children, version, date, iteration, builder);
                    break;
            }
        }
    }

    private static bool HasValue(TagPlaceholder kind, ThreePartVersion? version, int iteration) =>
        kind switch
        {
            TagPlaceholder.Prerelease => !string.IsNullOrEmpty(version?.PrereleaseLabel),
            TagPlaceholder.Iteration => iteration > 1,
            _ => true
        };

    private static string RenderPlaceholder(
        PlaceholderSegment placeholder,
        ThreePartVersion? version,
        DateTime date,
        int iteration)
    {
        switch (placeholder.Kind)
        {
            case TagPlaceholder.Major:
                return version!.Major.ToString(CultureInfo.InvariantCulture);
            case TagPlaceholder.Minor:
                return version!.Minor.ToString(CultureInfo.InvariantCulture);
            case TagPlaceholder.Patch:
                return version!.Patch.ToString(CultureInfo.InvariantCulture);
            case TagPlaceholder.Prerelease:
                return version?.PrereleaseLabel ?? string.Empty;
            case TagPlaceholder.Iteration:
                return iteration.ToString(CultureInfo.InvariantCulture);
            default:
                var format = placeholder.DateFormat ?? DefaultDateFormats[placeholder.Kind];
                // A one-character custom format string would otherwise be read as one of .NET's
                // standard date patterns ("d" meaning short-date, not day-of-month); "%" forces
                // the custom-specifier reading.
                return date.ToString(format.Length == 1 ? $"%{format}" : format, DateCulture);
        }
    }

    private static List<Segment> Tokenize(string format, string settingName)
    {
        var root = new List<Segment>();
        List<Segment>? optional = null;
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;

            (optional ?? root).Add(new LiteralSegment(literal.ToString()));
            literal.Clear();
        }

        var index = 0;
        while (index < format.Length)
        {
            var current = format[index];
            var next = index + 1 < format.Length ? format[index + 1] : '\0';

            // Doubling is the escape for all four structural characters.
            if ((current is '{' or '}' or '[' or ']') && next == current)
            {
                literal.Append(current);
                index += 2;
                continue;
            }

            switch (current)
            {
                case '[':
                    if (optional is not null)
                        throw new InvalidUserConfigurationException(
                            $"'{settingName}' nests optional groups in '{format}'. Nested '[...]' groups aren't supported.");
                    FlushLiteral();
                    optional = [];
                    index++;
                    continue;

                case ']':
                    if (optional is null)
                        throw new InvalidUserConfigurationException(
                            $"'{settingName}' has a ']' with no matching '[' in '{format}'.");
                    FlushLiteral();
                    if (!optional.OfType<PlaceholderSegment>().Any())
                        throw new InvalidUserConfigurationException(
                            $"'{settingName}' has an optional group with no placeholder in '{format}'. A '[...]' group " +
                            "is elided based on whether the placeholders inside it have a value, so it needs at least one.");
                    root.Add(new OptionalSegment(optional));
                    optional = null;
                    index++;
                    continue;

                case '}':
                    throw new InvalidUserConfigurationException(
                        $"'{settingName}' has a '}}' with no matching '{{' in '{format}'. Use '}}}}' for a literal '}}'.");

                case '{':
                    var close = format.IndexOf('}', index + 1);
                    if (close < 0)
                        throw new InvalidUserConfigurationException(
                            $"'{settingName}' has an unclosed '{{' in '{format}'.");

                    FlushLiteral();

                    var token = format.Substring(index + 1, close - index - 1);
                    var separator = token.IndexOf(':');
                    var name = separator < 0 ? token : token[..separator];
                    var dateFormat = separator < 0 ? null : token[(separator + 1)..];

                    if (!PlaceholderNames.TryGetValue(name.Trim(), out var kind))
                        throw new InvalidUserConfigurationException(
                            $"'{settingName}' uses an unknown placeholder '{{{token}}}' in '{format}'. Valid placeholders are " +
                            $"{string.Join(", ", PlaceholderNames.Keys.Select(key => $"{{{key}}}"))}.");

                    if (separator >= 0)
                    {
                        // Not the same as omitting the suffix: .NET reads an empty format string as
                        // the general date/time pattern, which would render something this format
                        // could never read back.
                        if (string.IsNullOrEmpty(dateFormat))
                            throw new InvalidUserConfigurationException(
                                $"'{settingName}' has an empty format after ':' in '{{{token}}}' in '{format}'. " +
                                $"Either give a date format (e.g. '{{{name}:yyyy-MM-dd}}') or drop the ':'.");

                        if (!DatePlaceholders.Contains(kind))
                            throw new InvalidUserConfigurationException(
                                $"'{settingName}' gives a format to '{{{name}}}' in '{format}', but only date placeholders " +
                                "({date}, {year}, {month}, {day}) accept one.");

                        // Validated eagerly so a bad specifier is reported against the setting that
                        // contains it, rather than surfacing later as an unmatchable tag.
                        DateFormatToPattern(dateFormat, name, settingName, format);
                    }

                    if (optional is not null && !OptionalCapablePlaceholders.Contains(kind))
                        throw new InvalidUserConfigurationException(
                            $"'{settingName}' puts '{{{name}}}' inside an optional group in '{format}'. Only " +
                            $"{string.Join(" and ", OptionalCapablePlaceholders.Select(placeholder => $"{{{placeholder.ToString().ToLowerInvariant()}}}"))} " +
                            "can be absent, so only those are meaningful in a '[...]' group.");

                    (optional ?? root).Add(new PlaceholderSegment(kind, dateFormat));
                    index = close + 1;
                    continue;

                default:
                    literal.Append(current);
                    index++;
                    continue;
            }
        }

        if (optional is not null)
            throw new InvalidUserConfigurationException(
                $"'{settingName}' has a '[' with no matching ']' in '{format}'.");

        FlushLiteral();
        return root;
    }

    private static IEnumerable<Segment> Flatten(IEnumerable<Segment> segments)
    {
        foreach (var segment in segments)
        {
            yield return segment;

            if (segment is OptionalSegment optional)
                foreach (var child in Flatten(optional.Children))
                    yield return child;
        }
    }

    private static string BuildPattern(IEnumerable<Segment> segments)
    {
        var pattern = new StringBuilder("^");
        AppendPattern(segments, pattern);
        pattern.Append('$');
        return pattern.ToString();
    }

    private static void AppendPattern(IEnumerable<Segment> segments, StringBuilder pattern)
    {
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case LiteralSegment literal:
                    pattern.Append(Regex.Escape(literal.Text));
                    break;

                case PlaceholderSegment placeholder:
                    pattern.Append(PlaceholderPattern(placeholder));
                    break;

                case OptionalSegment optional:
                    pattern.Append("(?:");
                    AppendPattern(optional.Children, pattern);
                    pattern.Append(")?");
                    break;
            }
        }
    }

    private static string PlaceholderPattern(PlaceholderSegment placeholder)
    {
        var group = GroupName(placeholder.Kind);

        return placeholder.Kind switch
        {
            TagPlaceholder.Major or TagPlaceholder.Minor or TagPlaceholder.Patch or TagPlaceholder.Iteration =>
                $@"(?<{group}>\d+)",
            // Lazy so that a trailing optional group still gets a chance to match: in
            // "v1.4.0-beta.1-2" a greedy prerelease would swallow "-2" and report iteration 1.
            // Allowed to match nothing so that a {prerelease} placed outside an optional group -
            // which renders as empty for a version that has no label - still reads back.
            TagPlaceholder.Prerelease =>
                $"(?<{group}>[0-9A-Za-z.-]*?)",
            _ =>
                $"(?<{group}>{DateFormatToPattern(placeholder.DateFormat ?? DefaultDateFormats[placeholder.Kind], placeholder.Kind.ToString().ToLowerInvariant(), string.Empty, string.Empty)})"
        };
    }

    /// <summary>
    /// Turns a .NET date format into a regex that matches what it renders, so a laid-out date can
    /// be found inside a tag. Deliberately covers a bounded set of specifiers and rejects the rest
    /// outright - a specifier that renders but can't be matched back would silently drop tags from
    /// release history.
    /// </summary>
    private static string DateFormatToPattern(string dateFormat, string placeholderName, string settingName, string format)
    {
        var pattern = new StringBuilder();
        var index = 0;

        while (index < dateFormat.Length)
        {
            var current = dateFormat[index];
            var run = 1;
            while (index + run < dateFormat.Length && dateFormat[index + run] == current)
                run++;

            var token = new string(current, run);

            switch (token)
            {
                case "yyyy":
                    pattern.Append(@"\d{4}");
                    break;
                case "yy":
                    pattern.Append(@"\d{2}");
                    break;
                case "MMMM":
                    pattern.Append("[A-Za-z]+");
                    break;
                case "MMM":
                    pattern.Append("[A-Za-z]{3}");
                    break;
                case "MM":
                    pattern.Append(@"\d{2}");
                    break;
                case "M":
                    pattern.Append(@"\d{1,2}");
                    break;
                case "dd":
                    pattern.Append(@"\d{2}");
                    break;
                case "d":
                    pattern.Append(@"\d{1,2}");
                    break;
                default:
                    if (current is '-' or '_' or '.' or '/' or ' ')
                    {
                        pattern.Append(Regex.Escape(token));
                        break;
                    }

                    throw new InvalidUserConfigurationException(
                        $"'{settingName}' uses an unsupported date specifier '{token}' in '{{{placeholderName}:{dateFormat}}}'" +
                        (string.IsNullOrEmpty(format) ? "" : $" in '{format}'") +
                        ". Supported specifiers are yyyy, yy, MMMM, MMM, MM, M, dd and d, plus the separators - _ . / and space.");
            }

            index += run;
        }

        return pattern.ToString();
    }

    private static string GroupName(TagPlaceholder kind) => kind.ToString().ToLowerInvariant();
}
