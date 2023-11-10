using System.CommandLine;

namespace AutoVer.Commands;

public interface ICommandFactory
{
    Command BuildRootCommand();
}

public class CommandFactory : ICommandFactory
{
    public Command BuildRootCommand()
    {
        // Name is important to set here to show correctly in the CLI usage help.
        var rootCommand = new RootCommand
        {
            Name = "autover",
            Description = "An automatic versioning tool for .NET"
        };

        return rootCommand;
    }
}