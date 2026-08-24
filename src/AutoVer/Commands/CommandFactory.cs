using System.CommandLine;
using AutoVer.Constants;
using AutoVer.Extensions;
using AutoVer.Models;
using AutoVer.Services;
using AutoVer.Services.IO;

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
    IVersionHandler versionHandler,
    IVersionIncrementer versionIncrementer,
    ICurrentDirectoryContext currentDirectoryContext
    ) : ICommandFactory
{
    private static readonly Option<string> OptionProjectPath = new("--project-path")
    {
        Description = "Path to the project",
        DefaultValueFactory = _ => Directory.GetCurrentDirectory()
    };
    private static readonly Option<string> OptionIncrementType = new("--increment-type")
    {
        Description = "Increment type. Available values: Major, Minor, Patch.",
        DefaultValueFactory = _ => IncrementType.Patch.ToString()
    };
    private static readonly Option<bool> OptionVerbose = new("--verbose")
    {
        Description = "Show full exception details, including inner exceptions, when a command fails."
    };
    // Resolves --project-path to an absolute path once, at the entry point, and returns that
    // resolved value for callers to use from here on. Every downstream consumer (GitHandler,
    // ProjectHandler, ConfigurationManager, ...) assumes it's always working with absolute
    // paths; forwarding the raw (possibly relative) CLI value instead would get re-resolved
    // a second time against the current directory this same call just set, double-applying any
    // relative segment (e.g. "proj" becoming ".../proj/proj").
    private string ResolveProjectPath(string? projectPath)
    {
        currentDirectoryContext.SetCurrentDirectory(projectPath);
        return currentDirectoryContext.CurrentDirectory;
    }

    private async Task<int> ExecuteCommandAsync(ParseResult parseResult, Func<Task> action)
    {
        try
        {
            await action();

            return CommandReturnCodes.Success;
        }
        catch (Exception e) when (e.IsExpectedException())
        {
            toolInteractiveService.WriteErrorLine(string.Empty);
            toolInteractiveService.WriteErrorLine(parseResult.GetValue(OptionVerbose) ? e.PrettyPrint() : e.Message);

            return CommandReturnCodes.UserError;
        }
        catch (Exception e)
        {
            // This is a bug
            toolInteractiveService.WriteErrorLine(
                "Unhandled exception.\r\nThis is a bug.\r\nPlease copy the stack trace below and file a bug at https://github.com/philasmar/autover. " +
                e.PrettyPrint());

            return CommandReturnCodes.UnhandledException;
        }
    }

    public Command BuildRootCommand()
    {
        var rootCommand = new RootCommand("An automatic versioning tool for .NET");

        rootCommand.Add(BuildVersionCommand());
        rootCommand.Add(BuildChangelogCommand());
        rootCommand.Add(BuildChangeCommand());

        return rootCommand;
    }

    private Command BuildVersionCommand()
    {
        var versionCommand = new Command(
            "version",
            "Perform automated versioning of the specified project(s).");

        Option<bool> skipVersionTagCheckOption = new("--skip-version-tag-check")
        {
            Description = $"Skip version tag check and increment projects even if some don't have a {ProjectConstants.VersionTag} tag."
        };
        Option<bool> noCommitOption = new("--no-commit") { Description = "Do not commit changes after versioning." };
        Option<bool> noTagOption = new("--no-tag") { Description = "Do not add a Git Tag after versioning." };
        Option<string> useVersionOption = new("--use-version") { Description = "Use a specific version for all projects." };
        Option<bool> currentOption = new("--current")
        {
            Description = "Print the current version of each project and exit, without incrementing, committing, or tagging anything."
        };

        versionCommand.Add(OptionProjectPath);
        versionCommand.Add(OptionIncrementType);
        versionCommand.Add(skipVersionTagCheckOption);
        versionCommand.Add(noCommitOption);
        versionCommand.Add(noTagOption);
        versionCommand.Add(useVersionOption);
        versionCommand.Add(currentOption);
        versionCommand.Add(OptionVerbose);

        versionCommand.SetAction((parseResult, cancellationToken) => ExecuteCommandAsync(parseResult, async () =>
        {
            var optionProjectPath = ResolveProjectPath(parseResult.GetValue(OptionProjectPath));
            var optionIncrementType = parseResult.GetValue(OptionIncrementType);
            var optionSkipVersionTagCheck = parseResult.GetValue(skipVersionTagCheckOption);
            var optionNoCommit = parseResult.GetValue(noCommitOption);
            var optionNoTag = parseResult.GetValue(noTagOption);
            var optionUseVersion = parseResult.GetValue(useVersionOption);
            var optionCurrent = parseResult.GetValue(currentOption);

            var command = new VersionCommand(
                projectHandler,
                gitHandler,
                configurationManager,
                changeFileHandler,
                versionHandler,
                versionIncrementer,
                toolInteractiveService);
            await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionSkipVersionTagCheck, optionNoCommit, optionNoTag, optionUseVersion, optionCurrent);
        }));

        return versionCommand;
    }

    private Command BuildChangelogCommand()
    {
        var changelogCommand = new Command(
            "changelog",
            "Create a changelog for the versioned repository.");

        Option<bool> outputToConsoleOption = new("--output-to-console") { Description = "Output the changelog to the console." };
        Option<bool> releaseNameOption = new("--release-name") { Description = "Gets the name of the current release." };
        Option<bool> tagNameOption = new("--tag-name") { Description = "Gets the name of the current GitHub tag." };

        changelogCommand.Add(OptionProjectPath);
        changelogCommand.Add(OptionIncrementType);
        changelogCommand.Add(outputToConsoleOption);
        changelogCommand.Add(releaseNameOption);
        changelogCommand.Add(tagNameOption);
        changelogCommand.Add(OptionVerbose);

        changelogCommand.SetAction((parseResult, cancellationToken) => ExecuteCommandAsync(parseResult, async () =>
        {
            var optionProjectPath = ResolveProjectPath(parseResult.GetValue(OptionProjectPath));
            var optionIncrementType = parseResult.GetValue(OptionIncrementType);
            var optionOutputToConsole = parseResult.GetValue(outputToConsoleOption);
            var optionReleaseName = parseResult.GetValue(releaseNameOption);
            var optionTagName = parseResult.GetValue(tagNameOption);

            var command = new ChangelogCommand(configurationManager, gitHandler, changelogHandler, toolInteractiveService, versionHandler);
            await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionOutputToConsole, optionReleaseName, optionTagName);
        }));

        return changelogCommand;
    }

    private Command BuildChangeCommand()
    {
        var changeCommand = new Command(
            "change",
            "Create a change file that contains information on the current changes.");

        Option<string> projectNameOption = new("--project-name") { Description = "The name of the project to add a change to." };
        Option<string> messageOption = new("--message", "-m") { Description = "The change message for a given project." };

        changeCommand.Add(OptionProjectPath);
        changeCommand.Add(OptionIncrementType);
        changeCommand.Add(projectNameOption);
        changeCommand.Add(messageOption);
        changeCommand.Add(OptionVerbose);

        changeCommand.SetAction((parseResult, cancellationToken) => ExecuteCommandAsync(parseResult, async () =>
        {
            var optionProjectPath = ResolveProjectPath(parseResult.GetValue(OptionProjectPath));
            var optionIncrementType = parseResult.GetValue(OptionIncrementType);
            var optionProjectName = parseResult.GetValue(projectNameOption);
            var optionMessage = parseResult.GetValue(messageOption);

            var command = new ChangeCommand(configurationManager, toolInteractiveService, changeFileHandler);
            await command.ExecuteAsync(optionProjectPath, optionIncrementType, optionProjectName, optionMessage);
        }));

        return changeCommand;
    }
}
