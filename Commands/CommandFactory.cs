using System.CommandLine;
using AutoVer.Services;

namespace AutoVer.Commands;

public interface ICommandFactory
{
    Command BuildRootCommand();
}

public class CommandFactory(
    IProjectHandler projectHandler,
    IToolInteractiveService toolInteractiveService,
    IVersionIncrementer versionIncrementer
    ) : ICommandFactory
{
    private static readonly Option<string> OptionProjectPath = new("--project-path", Directory.GetCurrentDirectory, "Path to the project");
    private static readonly Option<string> OptionNextVersion = new("--next-version", "Sets the next version");
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
            rootCommand.Add(BuildInfoCommand());
        }

        return rootCommand;
    }

    private Command BuildVersionCommand()
    {
        var versionCommand = new Command(
            "version",
            "Perform automated versioning of the specified project(s).");

        lock (ChildCommandLock)
        {
            versionCommand.Add(OptionProjectPath);
        }

        versionCommand.SetHandler(async (optionProjectPath) =>
        {
            var availableProjects = await projectHandler.GetAvailableProjects(optionProjectPath);
            foreach (var availableProject in availableProjects)
            {
                
            }
        }, OptionProjectPath);

        return versionCommand;
    }

    private Command BuildInfoCommand()
    {
        var infoCommand = new Command(
            "info",
            "Retrieve versioning information on the specified project(s).");
        
        lock (ChildCommandLock)
        {
            infoCommand.Add(OptionProjectPath);
            infoCommand.Add(OptionNextVersion);
        }
        
        infoCommand.SetHandler((optionProjectPath, optionNextVersion) =>
        {
            var command = new InfoCommand(
                projectHandler,
                toolInteractiveService,
                versionIncrementer);
            return command.ExecuteAsync(optionProjectPath, optionNextVersion);
        }, OptionProjectPath, OptionNextVersion);

        return infoCommand;
    }
}