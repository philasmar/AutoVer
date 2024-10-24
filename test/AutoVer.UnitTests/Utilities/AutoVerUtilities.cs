using AutoVer.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace AutoVer.UnitTests.Utilities;

internal static class AutoVerUtilities
{
    public static App InitializeApp()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddCustomServices();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        return serviceProvider.GetRequiredService<App>();
    }
}
