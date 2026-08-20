using AutoVer.Exceptions;

namespace AutoVer.Models;

public static class IncrementTypeParser
{
    public static IncrementType Parse(string? value)
    {
        // Matched by exact name only (case-insensitive) — deliberately not using
        // Enum.TryParse, which for a non-[Flags] enum still accepts comma-separated names
        // combined via bitwise OR (e.g. "Patch,Minor" => 1|2 == 3 == Major) and numeric
        // ordinals, neither of which are valid user input here.
        foreach (var name in Enum.GetNames<IncrementType>())
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<IncrementType>(name);
        }

        throw new InvalidArgumentException(
            $"The increment type '{value}' is invalid. Available values: {string.Join(", ", Enum.GetNames<IncrementType>())}.");
    }
}
