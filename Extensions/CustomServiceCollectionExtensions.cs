using AutoVer.Commands;
using AutoVer.Services;
using AutoVer.Services.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoVer.Extensions;

public static class CustomServiceCollectionExtensions
{
    public static void AddCustomServices(this IServiceCollection serviceCollection,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        serviceCollection.TryAdd(new ServiceDescriptor(typeof(ICommandFactory), typeof(CommandFactory), lifetime));
        serviceCollection.TryAdd(new ServiceDescriptor(typeof(IToolInteractiveService), typeof(ConsoleInteractiveService), lifetime));
        serviceCollection.TryAdd(new ServiceDescriptor(typeof(IDirectoryManager), typeof(DirectoryManager), lifetime));
        serviceCollection.TryAdd(new ServiceDescriptor(typeof(IFileManager), typeof(FileManager), lifetime));
        serviceCollection.TryAdd(new ServiceDescriptor(typeof(IProjectHandler), typeof(ProjectHandler), lifetime));
        serviceCollection.TryAdd(new ServiceDescriptor(typeof(IVersionIncrementer), typeof(ThreePartVersionIncrementer), lifetime));
        
        serviceCollection.AddSingleton<App>();
    }
}