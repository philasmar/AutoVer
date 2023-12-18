namespace AutoVer.Models;

public class ConventionalCommit
{
    public required string Type { get; set; }
    
    public string? Scope { get; set; }
    
    public required string Description { get; set; }
    
    public string? Body { get; set; }
    
    public required bool IsBreakingChange { get; set; }
}