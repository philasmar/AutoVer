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
    IChangelogHandler changelogHandler,
    IChangeFileHandler changeFileHandler,
    IVersionHandler versionHandler
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
            rootCommand.Add(BuildChangeCommand());
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
                
                var command = new VersionCommand(projectHandler, gitHandler, configurationManager, changeFileHandler, versionHandler);
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
        Option<bool> releaseNameOption = new(new[] { "--release-name" }, $"Gets the name of the current release.");
        Option<bool> tagNameOption = new(new[] { "--tag-name" }, $"Gets the name of the current GitHub tag.");

        lock (ChildCommandLock)
        {
            changelogCommand.Add(OptionProjectPath);
            changelogCommand.Add(OptionIncrementType);
            changelogCommand.Add(outputToConsoleOption);
            changelogCommand.Add(releaseNameOption);
            changelogCommand.Add(tagNameOption);
        }

        changelogCommand.SetHandler(async (context) =>
        {
            try
            {
                var optionProjectPath = context.ParseResult.GetValueForOption(OptionProjectPath);
                var optionIncrementType = context.ParseResult.GetValueForOption(OptionIncrementType);
                var optionOutputToConsole = context.ParseResult.GetValueForOption(outputToConsoleOption);
                var optionReleaseName = context.ParseResult.GetValueForOption(releaseNameOption);
                var optionTagName = context.ParseResult.GetValueForOption(tagNameOption);
                
                var command = new ChangelogCommand(configurationManager, gitHandler, changelogHandler, toolInteractiveService, versionHandler);
                await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionOutputToConsole, optionReleaseName, optionTagName);
                    
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

    private Command BuildChangeCommand()
    {
        var changeCommand = new Command(
            "change",
            "Create a change file that contains information on the current changes.");
        
        Option<string> projectNameOption = new(["--project-name"], "The name of the project to add a change to.");
        Option<string> messageOption = new(["-m", "--message"], "The change message for a given project.");
        
        lock (ChildCommandLock)
        {
            changeCommand.Add(OptionProjectPath);
            changeCommand.Add(OptionIncrementType);
            changeCommand.Add(projectNameOption);
            changeCommand.Add(messageOption);
        }
        
        changeCommand.SetHandler(async (context) =>
        {
            try
            {
                var optionProjectPath = context.ParseResult.GetValueForOption(OptionProjectPath);
                var optionIncrementType = context.ParseResult.GetValueForOption(OptionIncrementType);
                var optionProjectName = context.ParseResult.GetValueForOption(projectNameOption);
                var optionMessage = context.ParseResult.GetValueForOption(messageOption);
                
                var command = new ChangeCommand(configurationManager, toolInteractiveService, changeFileHandler);
                await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionProjectName, optionMessage);
                    
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

        return changeCommand;
    }
}