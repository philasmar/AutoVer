using System.Text;
using AutoVer.Exceptions;
using AutoVer.Models;

namespace AutoVer.Services;

public class ChangelogHandler(
    IGitHandler gitHandler) : IChangelogHandler
{
    public string GenerateChangelogAsMarkdown(UserConfiguration configuration, string nextVersion)
    {
        var changelog = new StringBuilder();
        changelog.AppendLine($"## Release {nextVersion}");
        
        if (configuration.UseCommitsForChangelog)
        {
            
            // var tags = gitHandler.GetTags(gitRoot);
            // var versionNumbers = tags
            //     .Where(x => x.StartsWith("v"))
            //     .Select(x => x.Replace("release_", ""))
            //     .Select(x =>
            //     {
            //         DateTime.TryParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            //             out var date);
            //         return date;
            //     })
            //     .ToList();
            // var nextVersionNumber = versionNumbers.Any() ? versionNumbers.Max() + 1 : 1;
            var commits = gitHandler.GetVersionCommits(configuration.GitRoot, nextVersion);
        }
        else
        {
            foreach (var project in configuration.Projects)
            {
                if (project.ProjectDefinition is null)
                    throw new InvalidProjectException($"The project '{project.Path}' is invalid.");
                
                if (project.Changelog.Count == 0)
                    continue;
                
                var projectName = GetProjectName(project.ProjectDefinition.ProjectPath);
                changelog.AppendLine();
                changelog.AppendLine($"### {projectName}");

                changelog.AppendLine();
                foreach (var entry in project.Changelog)
                {
                    changelog.AppendLine($"* {entry}");
                }
            }
        }

        return changelog.ToString();
    }

    private string GetProjectName(string projectPath)
    {
        var projectParts = projectPath.Split(Path.DirectorySeparatorChar);
        if (projectParts.Length == 0)
            throw new InvalidProjectException($"The project '{projectPath}' is invalid.");
        var projectFileName = projectParts.Last();
        var projectFileNameParts = projectFileName.Split('.');
        if (projectFileNameParts.Length != 2)
            throw new InvalidProjectException($"The project '{projectPath}' is invalid.");
        return projectFileNameParts.First();
    }
}