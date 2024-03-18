using System.Text.Json.Serialization;

namespace AutoVer.Models;

public class UserConfiguration
{
    internal string? GitRoot { get; set; }
    internal bool PersistConfiguration { get; set; }
    public List<Project> Projects { get; set; } = [];
    
    public bool UseCommitsForChangelog { get; set; } = true;

    public Dictionary<string, string>? ChangelogCategories { get; set; }
    
    public class Project
    {
        public required string Name { get; set; }
        public required string Path { get; set; }
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IncrementType IncrementType { get; set; } = IncrementType.Patch;
        
        public string? PrereleaseLabel { get; set; }
    
        internal ProjectDefinition? ProjectDefinition { get; set; }
    }
}