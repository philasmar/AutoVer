using AutoVer.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace AutoVer.IntegrationTests.Utilities;

internal static class AutoVerUtilities
{
    public static App InitializeApp()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddCustomServices();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        return serviceProvider.GetRequiredService<App>();
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunCapturingOutput(string[] args)
    {
        var app = InitializeApp();

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        // The whole assembly runs with [assembly: NotInParallel], so this process-wide
        // redirection can't race with another test.
#pragma warning disable TUnit0055
        Console.SetOut(outWriter);
        Console.SetError(errorWriter);
#pragma warning restore TUnit0055

        int exitCode;
        try
        {
            exitCode = await app.Run(args);
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
            Console.SetError(originalError);
#pragma warning restore TUnit0055
        }

        return (exitCode, outWriter.ToString(), errorWriter.ToString());
    }
}
