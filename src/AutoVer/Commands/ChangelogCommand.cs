using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class ChangelogCommand(
    IConfigurationManager configurationManager,
    IGitHandler gitHandler,
    IChangelogHandler changelogHandler,
    IToolInteractiveService toolInteractiveService)
{
    public async Task ExecuteAsync(string? optionProjectPath, string? optionIncrementType, bool optionOutputToConsole)
    {
        if (!Enum.TryParse(optionIncrementType, out IncrementType incrementType))
        {
            incrementType = IncrementType.Patch;
        }
        
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(optionProjectPath, incrementType);
        
        var changelog = changelogHandler.GenerateChangelogAsMarkdown(userConfiguration);
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