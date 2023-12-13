using System.Text.Json.Serialization;

namespace AutoVer.Models;

public class UserConfiguration
{
    internal string? GitRoot { get; set; }
    public List<Project> Projects { get; set; } = [];
    
    public bool UseCommitsForChangelog { get; set; } = true;
    
    public class Project
    {
        public required string Path { get; set; }
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IncrementType IncrementType { get; set; } = IncrementType.Patch;
        
        public List<string> Changelog { get; set; } = [];
    
        internal ProjectDefinition? ProjectDefinition { get; set; }
    }
}