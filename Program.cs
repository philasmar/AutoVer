using AutoVer;
using AutoVer.Extensions;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();

serviceCollection.AddCustomServices();

var serviceProvider = serviceCollection.BuildServiceProvider();

// calls the Run method in App, which is replacing Main
var app = serviceProvider.GetService<App>();
if (app == null)
{
    throw new Exception("App dependencies aren't injected correctly." +
                        " Verify CustomServiceCollectionExtension has all the required dependencies to instantiate App.");
}

return await app.Run(args);