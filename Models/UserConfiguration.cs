using System.Text.Json.Serialization;

namespace AutoVer.Models;

public class UserConfiguration
{
    public List<Project> Projects { get; set; } = [];
    
    public class Project
    {
        public required string Path { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IncrementType IncrementType { get; set; } = IncrementType.Patch;
        public bool UseCommitsForChangelog { get; set; } = true;
        public List<string> Changelog { get; set; } = [];
    
        internal ProjectDefinition? ProjectDefinition { get; set; }
    }
}