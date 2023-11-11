using System.CommandLine;
using AutoVer.Services;

namespace AutoVer.Commands;

public interface ICommandFactory
{
    Command BuildRootCommand();
}

public class CommandFactory(
    IProjectHandler projectHandler
    ) : ICommandFactory
{
    private static readonly Option<string> OptionProjectPath = new("--project-path", Directory.GetCurrentDirectory, "Path to the project");
    private static readonly object RootCommandLock = new();
    private static readonly object ChildCommandLock = new();
    
    public Command BuildRootCommand()
    {
        // Name is important to set here to show correctly in the CLI usage help.
        var rootCommand = new RootCommand
        {
            Name = "autover",
            Description = "An automatic versioning tool for .NET"
        };
        
        lock(RootCommandLock)
        {
            rootCommand.Add(BuildVersionCommand());
        }

        return rootCommand;
    }

    private Command BuildVersionCommand()
    {
        var versionCommand = new Command(
            "version",
            "Perform automated versioning of the specified project.");

        lock (ChildCommandLock)
        {
            versionCommand.Add(OptionProjectPath);
        }

        versionCommand.SetHandler(async (optionProjectPath) =>
        {
            var availableProjects = await projectHandler.GetAvailableProjects(optionProjectPath);
        }, OptionProjectPath);

        return versionCommand;
    }
}