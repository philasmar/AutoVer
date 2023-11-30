using System.CommandLine;
using AutoVer.Constants;
using AutoVer.Extensions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public interface ICommandFactory
{
    Command BuildRootCommand();
}

public class CommandFactory(
    IProjectHandler projectHandler,
    IToolInteractiveService toolInteractiveService,
    IVersionIncrementer versionIncrementer,
    IGitHandler gitHandler,
    IConfigurationManager configurationManager
    ) : ICommandFactory
{
    private static readonly Option<string> OptionProjectPath = new("--project-path", Directory.GetCurrentDirectory, "Path to the project");
    private static readonly Option<string> OptionIncrementType = new("--increment-type", IncrementType.Patch.ToString, "Increment type. Available values: Major, Minor, Patch.");
    private static readonly Option<bool> OptionSkipVersionTagCheck = new(new[] { "--skip-version-tag-check" }, $"Skip version tag check and increment projects even if some don't have a {ProjectConstants.VersionTag} tag.");
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
            rootCommand.Add(BuildConfigureCommand());
            rootCommand.Add(BuildVersionCommand());
            rootCommand.Add(BuildInfoCommand());
        }

        return rootCommand;
    }

    private Command BuildConfigureCommand()
    {
        var configureCommand = new Command(
            "configure",
            "Configure AutoVer to set versioning preferences.");
        
        configureCommand.SetHandler(async () =>
        {
            var command = new ConfigureCommand();
            await command.ExecuteAsync();
        });
        
        return configureCommand;
    }

    private Command BuildVersionCommand()
    {
        var versionCommand = new Command(
            "version",
            "Perform automated versioning of the specified project(s).");

        lock (ChildCommandLock)
        {
            versionCommand.Add(OptionProjectPath);
            versionCommand.Add(OptionIncrementType);
            versionCommand.Add(OptionSkipVersionTagCheck);
        }

        versionCommand.SetHandler(async (context) =>
        {
            try
            {
                var optionProjectPath = context.ParseResult.GetValueForOption(OptionProjectPath);
                var optionIncrementType = context.ParseResult.GetValueForOption(OptionIncrementType);
                var optionSkipVersionTagCheck = context.ParseResult.GetValueForOption(OptionSkipVersionTagCheck);
                
                var command = new VersionCommand(projectHandler, gitHandler, configurationManager);
                await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionSkipVersionTagCheck);
                    
                context.ExitCode = CommandReturnCodes.Success;
            }
            catch (Exception e) when (e.IsExpectedException())
            {
                toolInteractiveService.WriteErrorLine(string.Empty);
                toolInteractiveService.WriteErrorLine(e.Message);
                    
                context.ExitCode = CommandReturnCodes.UserError;
            }
            catch (Exception e)
            {
                // This is a bug
                toolInteractiveService.WriteErrorLine(
                    "Unhandled exception.\r\nThis is a bug.\r\nPlease copy the stack trace below and file a bug at https://github.com/philasmar/autover. " +
                    e.PrettyPrint());
                    
                context.ExitCode = CommandReturnCodes.UnhandledException;
            }
        });

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
        }
        
        infoCommand.SetHandler((optionProjectPath) =>
        {
            var command = new InfoCommand(
                projectHandler,
                toolInteractiveService,
                versionIncrementer,
                gitHandler);
            return command.ExecuteAsync(optionProjectPath);
        }, 
            OptionProjectPath);

        return infoCommand;
    }
}