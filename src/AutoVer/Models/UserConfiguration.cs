using System.Text.Json.Serialization;

namespace AutoVer.Models;

public class UserConfiguration
{
    [JsonIgnore] public string GitRoot { get; set; } = string.Empty;
    internal bool PersistConfiguration { get; set; }
    public List<Project> Projects { get; set; } = [];
    public bool UseCommitsForChangelog { get; set; } = true;
    public bool UseSameVersionForAllProjects { get; set; } = false;
        
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IncrementType DefaultIncrementType { get; set; } = IncrementType.Patch;
    public Dictionary<string, string>? ChangelogCategories { get; set; }
    public bool ChangeFilesDetermineIncrementType { get; set; } = false;
    
    public class Project
    {
        public required string Name { get; set; }
        public required string Path { get; set; }
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IncrementType? IncrementType { get; set; }
        
        public string? PrereleaseLabel { get; set; }
    
        internal ProjectDefinition? ProjectDefinition { get; set; }
    }
}