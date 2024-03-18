using AutoVer.Models;
using AutoVer.Services;

namespace AutoVer.Commands;

public class ChangeCommand(
    IConfigurationManager configurationManager,
    IToolInteractiveService toolInteractiveService,
    IChangeFileHandler changeFileHandler)
{
    public async Task ExecuteAsync(
        string? optionProjectPath, 
        string? optionIncrementType,
        string? optionMessage)
    {
        if (!Enum.TryParse(optionIncrementType, out IncrementType incrementType))
        {
            incrementType = IncrementType.Patch;
        }
        
        if (string.IsNullOrEmpty(optionProjectPath))
            optionProjectPath = Directory.GetCurrentDirectory();
        
        var userConfiguration = await configurationManager.RetrieveUserConfiguration(optionProjectPath, incrementType);
        if (userConfiguration.UseCommitsForChangelog)
        {
            toolInteractiveService.WriteErrorLine($"This repository is not configured to use change files. Change '{nameof(userConfiguration.UseCommitsForChangelog)}' to 'false' in the repo's '.autover/autover.json' file.");
            return;
        }
        if (userConfiguration.Projects.Count > 1 && !string.IsNullOrEmpty(optionMessage))
        {
            toolInteractiveService.WriteErrorLine("You need to specify a project name with the change message. Use the '--project-name' argument to specify the project name.");
            return;
        }

        var changeFile = changeFileHandler.GenerateChangeFile(userConfiguration, optionMessage);

        await changeFileHandler.PersistChangeFile(userConfiguration, changeFile);
    }
}