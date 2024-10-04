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
        var fullVersionParts = version?.Split("-");
        var prereleaseLabel = (fullVersionParts?.Length == 2) ? fullVersionParts[1] : null;
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

        return string.Compare(PrereleaseLabel, other.PrereleaseLabel, StringComparison.Ordinal);
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