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
    IGitHandler gitHandler,
    IConfigurationManager configurationManager,
    IChangelogHandler changelogHandler
    ) : ICommandFactory
{
    private static readonly Option<string> OptionProjectPath = new("--project-path", Directory.GetCurrentDirectory, "Path to the project");
    private static readonly Option<string> OptionIncrementType = new("--increment-type", IncrementType.Patch.ToString, "Increment type. Available values: Major, Minor, Patch.");
    private static readonly object RootCommandLock = new();
    private static readonly object ChildCommandLock = new();
    
    public Command BuildRootCommand()
    {
        // Name is important to set here to show correctly in the CLI usage help.
        var rootCommand = new RootCommand
        {
            Name = "AutoVer",
            Description = "An automatic versioning tool for .NET"
        };
        
        lock(RootCommandLock)
        {
            rootCommand.Add(BuildVersionCommand());
            rootCommand.Add(BuildChangelogCommand());
        }

        return rootCommand;
    }

    private Command BuildVersionCommand()
    {
        var versionCommand = new Command(
            "version",
            "Perform automated versioning of the specified project(s).");
    
        Option<bool> skipVersionTagCheckOption = new(new[] { "--skip-version-tag-check" }, $"Skip version tag check and increment projects even if some don't have a {ProjectConstants.VersionTag} tag.");
        Option<bool> noCommitOption = new(new[] { "--no-commit" }, $"Do not commit changes after versioning.");
        Option<bool> noTagOption = new(new[] { "--no-tag" }, $"Do not add a Git Tag after versioning.");

        lock (ChildCommandLock)
        {
            versionCommand.Add(OptionProjectPath);
            versionCommand.Add(OptionIncrementType);
            versionCommand.Add(skipVersionTagCheckOption);
            versionCommand.Add(noCommitOption);
            versionCommand.Add(noTagOption);
        }

        versionCommand.SetHandler(async (context) =>
        {
            try
            {
                var optionProjectPath = context.ParseResult.GetValueForOption(OptionProjectPath);
                var optionIncrementType = context.ParseResult.GetValueForOption(OptionIncrementType);
                var optionSkipVersionTagCheck = context.ParseResult.GetValueForOption(skipVersionTagCheckOption);
                var optionNoCommit = context.ParseResult.GetValueForOption(noCommitOption);
                var optionNoTag = context.ParseResult.GetValueForOption(noTagOption);
                
                var command = new VersionCommand(projectHandler, gitHandler, configurationManager);
                await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionSkipVersionTagCheck, optionNoCommit, optionNoTag);
                    
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

    private Command BuildChangelogCommand()
    {
        var changelogCommand = new Command(
            "changelog",
            "Create a changelog for the versioned repository.");
    
        Option<bool> outputToConsoleOption = new(new[] { "--output-to-console" }, $"Output the changelog to the console.");

        lock (ChildCommandLock)
        {
            changelogCommand.Add(OptionProjectPath);
            changelogCommand.Add(OptionIncrementType);
            changelogCommand.Add(outputToConsoleOption);
        }

        changelogCommand.SetHandler(async (context) =>
        {
            try
            {
                var optionProjectPath = context.ParseResult.GetValueForOption(OptionProjectPath);
                var optionIncrementType = context.ParseResult.GetValueForOption(OptionIncrementType);
                var optionOutputToConsole = context.ParseResult.GetValueForOption(outputToConsoleOption);
                
                var command = new ChangelogCommand(configurationManager, gitHandler, changelogHandler, toolInteractiveService);
                await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionOutputToConsole);
                    
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

        return changelogCommand;
    }
}