using System.Text;

namespace AutoVer.Models;

public class ChangelogEntry
{
    public required string Title { get; set; }
    public required string TagName { get; set; }
    public List<ChangelogCategory> ChangelogCategories { get; set; } = [];

    public string ToMarkdown()
    {
        var changelog = new StringBuilder();
        changelog.AppendLine($"## {Title}");
        changelog.AppendLine();
        foreach (var changelogCategory in ChangelogCategories)
        {
            changelog.AppendLine($"### {changelogCategory.Name}");
            foreach (var change in changelogCategory.Changes)
            {
                if (string.IsNullOrEmpty(change.Scope))
                {
                    changelog.AppendLine($"* {change.Description}");
                }
                else
                {
                    changelog.AppendLine($"* **{change.Scope}**: {change.Description}");
                }
            }
        }
        
        return changelog.ToString();
    }
}

public class ChangelogCategory
{
    public required string Name { get; set; }
    public List<ChangelogChange> Changes { get; set; } = [];
}

public class ChangelogChange
{
    public required string Description { get; set; }
    public string? Scope { get; set; }
}