using AutoVer.Models.CommandInputs;

namespace AutoVer.Commands;

public class ConfigureCommand(
    ConfigureCommandInput input)
{
    public async Task ExecuteAsync()
    {
        await Task.CompletedTask;
    }
}