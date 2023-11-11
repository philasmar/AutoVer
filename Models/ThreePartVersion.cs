namespace AutoVer.Models;

public class ThreePartVersion
{
    public required int Major { get; set; }
    public required int Minor { get; set; }
    public required int Patch { get; set; }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }

    public static ThreePartVersion Parse(string? version)
    {
        var versionParts = version?.Split(".");
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
            Patch = patch
        };
    }
}