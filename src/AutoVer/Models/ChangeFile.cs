namespace AutoVer.Models;

public class ChangeFile
{
    public List<ProjectChange> Projects { get; set; } = [];
}

public class ProjectChange
{
    public required string Name { get; set; }
            
    public List<string> ChangelogMessages { get; set; } = [];
}