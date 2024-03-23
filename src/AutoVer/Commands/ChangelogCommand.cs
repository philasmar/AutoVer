using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class ChangelogCommand(
    IConfigurationManager configurationManager,
    IGitHandler gitHandler,
    IChangelogHandler changelogHandler,
    IToolInteractiveService toolInteractiveService,
    IVersionHandler versionHandler)
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

        var tagName = versionHandler.GetCurrentVersionTag(optionProjectPath);
        
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(optionProjectPath, incrementType, tagName);
        
        var changelogEntry = await changelogHandler.GenerateChangelog(userConfiguration);
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