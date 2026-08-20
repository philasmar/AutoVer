using AutoVer.Exceptions;

namespace AutoVer.Extensions;

public static class ProjectExtensions
{
    public static void EnsureProjectHasVersionTag(this Models.ProjectContainer projectContainer)
    {
        foreach (var project in projectContainer.Projects)
        {
            if (string.IsNullOrEmpty(project.ProjectDefinition.Version))
                throw new NoVersionTagException($"The project '{projectContainer.Name}' does not have a version defined. Add one and run the tool again.");
        }
    }
}
