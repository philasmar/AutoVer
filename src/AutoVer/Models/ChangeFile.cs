using System.Text.Json.Serialization;

namespace AutoVer.Models;

public class ChangeFile
{
    internal string? Path { get; set; }
    public List<ProjectChange> Projects { get; set; } = [];
}

public class ProjectChange
{
    public required string Name { get; set; }
        
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IncrementType? Type { get; set; }
            
    public List<string> ChangelogMessages { get; set; } = [];
}