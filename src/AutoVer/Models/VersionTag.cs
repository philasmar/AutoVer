namespace AutoVer.Models;

/// <summary>
/// A git tag that matched the repository's configured tag format, decomposed into the components
/// that order it. Ordering matters beyond presentation: the most recent tag identifies the current
/// release, and the one before it bounds the commit range a changelog covers.
/// </summary>
public sealed class VersionTag(
    string raw,
    TagFormatFamily family,
    ThreePartVersion? version,
    DateTime? date,
    int iteration) : IComparable<VersionTag>
{
    /// <summary>
    /// The tag exactly as it exists in git.
    /// </summary>
    public string Raw { get; } = raw;

    public TagFormatFamily Family { get; } = family;

    /// <summary>
    /// Set for a <see cref="TagFormatFamily.Semver"/> tag.
    /// </summary>
    public ThreePartVersion? Version { get; } = version;

    /// <summary>
    /// Set for a <see cref="TagFormatFamily.Date"/> tag.
    /// </summary>
    public DateTime? Date { get; } = date;

    /// <summary>
    /// 1 for the first release of a given version/date, incrementing only to break a collision.
    /// </summary>
    public int Iteration { get; } = iteration;

    public int CompareTo(VersionTag? other)
    {
        if (other is null)
            return 1;

        var result = Family == TagFormatFamily.Semver
            ? Comparer<ThreePartVersion?>.Default.Compare(Version, other.Version)
            : Nullable.Compare(Date, other.Date);

        return result != 0 ? result : Iteration.CompareTo(other.Iteration);
    }
}
