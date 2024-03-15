using System.Globalization;
using AutoVer.Exceptions;
using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class ChangelogCommand(
    IConfigurationManager configurationManager,
    IGitHandler gitHandler,
    IChangelogHandler changelogHandler,
    IToolInteractiveService toolInteractiveService)
{
    public async Task ExecuteAsync(
        string? optionProjectPath, 
        string? optionIncrementType, 
        bool optionOutputToConsole, 
        bool optionReleaseName,
        bool optionTagName)
    {
        if (!Enum.TryParse(optionIncrementType, out IncrementType incrementType))
        {
            incrementType = IncrementType.Patch;
        }
        
        if (string.IsNullOrEmpty(optionProjectPath))
            optionProjectPath = Directory.GetCurrentDirectory();
        var gitRoot = gitHandler.FindGitRootDirectory(optionProjectPath);
        
        var tags = gitHandler.GetTags(gitRoot);
        var versionNumbers = tags
            .Where(x => x.StartsWith("version_"))
            .Select(x => x.Replace("version_", ""))
            .Select(x => DateTime.ParseExact(x, "yyyy-MM-dd.HH.mm.ss", CultureInfo.InvariantCulture))
            .OrderDescending()
            .ToList();
        if (versionNumbers.Count == 0)
            throw new InvalidVersionTag($"The Git repository '{gitRoot}' does not have a valid version tag. Please run 'autover version' first.");
        var currentVersionDate = versionNumbers[0];

        var tagName = $"version_{currentVersionDate:yyyy-MM-dd.HH.mm.ss}";
        
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(optionProjectPath, incrementType, tagName);
        
        var changelogEntry = changelogHandler.GenerateChangelog(userConfiguration);
        if (optionReleaseName)
        {
            toolInteractiveService.WriteLine(changelogEntry.Title);
            return;
        }
        if (optionTagName)
        {
            toolInteractiveService.WriteLine(changelogEntry.TagName);
            return;
        }
        var changelog = changelogEntry.ToMarkdown();
        if (optionOutputToConsole)
        {
            toolInteractiveService.WriteLine(changelog);
            return;
        }
        
        await changelogHandler.PersistChangelog(userConfiguration, changelog, null);
        
        // When done, reset the config file if the user had one
        if (userConfiguration.PersistConfiguration)
        {
            await configurationManager.ResetUserConfiguration(userConfiguration, new UserConfigurationResetRequest
            {
                Changelog = true
            });
            
            gitHandler.CommitChanges(userConfiguration, $"Updated changelog");
        }
    }
}