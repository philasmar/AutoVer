using System.CommandLine;
using System.Text;
using AutoVer.Commands;
using AutoVer.Services;

namespace AutoVer;

public class App(
    ICommandFactory commandFactory, 
    IToolInteractiveService toolInteractiveService)
{
    public async Task<int> Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // if user didn't specify a command, default to help
        if (args.Length == 0)
        {
            toolInteractiveService.WriteLine("An automatic versioning tool for .NET");
            toolInteractiveService.WriteLine("Project Home: https://github.com/philasmar/autover");
            toolInteractiveService.WriteLine(string.Empty);
            
            args = new[] { "-h" };
        }

        return await commandFactory.BuildRootCommand().InvokeAsync(args);
    }
}