namespace AutoVer.Models;

public class ThreePartVersion : IComparable<ThreePartVersion>
{
    public required int Major { get; set; }
    public required int Minor { get; set; }
    public required int Patch { get; set; }
    public string? PrereleaseLabel { get; set; }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(PrereleaseLabel))
        {
            return $"{Major}.{Minor}.{Patch}";
        }
        else
        {
            return $"{Major}.{Minor}.{Patch}-{PrereleaseLabel}";
        }
    }

    public static ThreePartVersion Parse(string? version)
    {
        // Per SemVer the prerelease is everything after the *first* hyphen and may itself contain
        // hyphens (1.0.0-alpha-1). Splitting on every hyphen produced more than two parts for those
        // and dropped the label altogether - which a version-based tag format would then bake into
        // the tag it renders.
        var fullVersionParts = version?.Split('-', 2);
        var prereleaseLabel = fullVersionParts?.Length == 2 && !string.IsNullOrEmpty(fullVersionParts[1])
            ? fullVersionParts[1]
            : null;
        var versionParts = fullVersionParts?[0].Split(".");
        if (versionParts?.Length != 3)
            throw new Exception("The provided version number is not a valid 3 part version.");
        
        if (!int.TryParse(versionParts[0], out var major) ||
            !int.TryParse(versionParts[1], out var minor) ||
            !int.TryParse(versionParts[2], out var patch))
            throw new Exception("The provided version number is not a valid 3 part version.");
        
        return new ThreePartVersion
        {
            Major = major,
            Minor = minor,
            Patch = patch,
            PrereleaseLabel = prereleaseLabel
        };
    }

    public static bool TryParse(string? versionString, out ThreePartVersion version)
    {
        try
        {
            version = Parse(versionString);
            return true;
        }
        catch (Exception)
        {
            version = new ThreePartVersion
            {
                Major = 0,
                Minor = 0,
                Patch = 1
            };
            return false;
        }
    }
    
    public int CompareTo(ThreePartVersion? other)
    {
        if (other == null) return 1;

        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        return ComparePrerelease(PrereleaseLabel, other.PrereleaseLabel);
    }

    /// <summary>
    /// Prerelease precedence per SemVer 2.0.0 §11: a version carrying a prerelease label ranks
    /// below the same version without one, and labels compare identifier-by-identifier (split on
    /// '.') with numeric identifiers compared numerically. A plain string comparison gets both
    /// wrong - it puts 1.4.0 below 1.4.0-beta.1, and beta.10 below beta.2.
    /// </summary>
    private static int ComparePrerelease(string? left, string? right)
    {
        var leftMissing = string.IsNullOrEmpty(left);
        var rightMissing = string.IsNullOrEmpty(right);

        if (leftMissing && rightMissing) return 0;
        if (leftMissing) return 1;
        if (rightMissing) return -1;

        var leftIdentifiers = left!.Split('.');
        var rightIdentifiers = right!.Split('.');

        for (var i = 0; i < Math.Max(leftIdentifiers.Length, rightIdentifiers.Length); i++)
        {
            // "A larger set of pre-release fields has a higher precedence than a smaller set, if
            // all of the preceding identifiers are equal" - so beta.1 ranks below beta.1.1.
            if (i >= leftIdentifiers.Length) return -1;
            if (i >= rightIdentifiers.Length) return 1;

            var leftIdentifier = leftIdentifiers[i];
            var rightIdentifier = rightIdentifiers[i];

            var leftIsNumeric = long.TryParse(leftIdentifier, out var leftNumber);
            var rightIsNumeric = long.TryParse(rightIdentifier, out var rightNumber);

            if (leftIsNumeric && rightIsNumeric)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0) return numeric;
                continue;
            }

            // "Numeric identifiers always have lower precedence than non-numeric identifiers."
            if (leftIsNumeric) return -1;
            if (rightIsNumeric) return 1;

            var ordinal = string.CompareOrdinal(leftIdentifier, rightIdentifier);
            if (ordinal != 0) return ordinal;
        }

        return 0;
    }
    
    public static bool operator >(ThreePartVersion? left, ThreePartVersion? right)
    {
        if (left is null) return false;
        return left.CompareTo(right) > 0;
    }

    public static bool operator <(ThreePartVersion? left, ThreePartVersion? right)
    {
        if (left is null) return right is not null;
        return left.CompareTo(right) < 0;
    }

    public static bool operator >=(ThreePartVersion? left, ThreePartVersion? right)
    {
        if (left is null) return right is null;
        return left.CompareTo(right) >= 0;
    }

    public static bool operator <=(ThreePartVersion? left, ThreePartVersion? right)
    {
        if (left is null) return true;
        return left.CompareTo(right) <= 0;
    }
}