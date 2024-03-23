namespace AutoVer.Constants;

public abstract class ChangelogConstants
{
    public const string DefaultChangelogFileName = "CHANGELOG.md";

    public static readonly Dictionary<string, string> ChangelogCategories = new()
    {
        {"build", "Build System"},
        {"chore", "Chores"},
        {"ci", "Continuous Integration"},
        {"docs", "Documentation"},
        {"feat", "Features"},
        {"fix", "Bug Fixes"},
        {"perf", "Performance"},
        {"refactor", "Refactor"},
        {"revert", "Revert"},
        {"style", "Style"},
        {"test", "Testing"}
    };
}