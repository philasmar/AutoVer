using AutoVer.Models;
using AutoVer.Services.IO;
using AutoVer.Services.ProjectFiles;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoVer.Services.Converters;

public class UserConfigurationConverter(
    IFileManager fileManager,
    IPathManager pathManager,
    IProjectFileHandlerResolver projectFileHandlerResolver) : JsonConverter<UserConfiguration>
{
    public override UserConfiguration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var defaultOptions = new JsonSerializerOptions(options);

        defaultOptions.Converters.Remove(this);

        UserConfiguration? obj = JsonSerializer.Deserialize<UserConfiguration>(ref reader, defaultOptions);

        // Inject the dependency into the deserialized object
        if (obj != null)
        {
            foreach (var container in obj.Projects)
            {
                foreach (var project in container.Projects)
                {
                    project.InjectDependency(fileManager, pathManager, projectFileHandlerResolver);
                    project.OnDeserialized();
                }
                container.InjectDependency(fileManager, pathManager, projectFileHandlerResolver);
                container.OnDeserialized();
            }
        }

        return obj;
    }

    public override void Write(Utf8JsonWriter writer, UserConfiguration value, JsonSerializerOptions options)
    {
        var defaultOptions = new JsonSerializerOptions(options);

        defaultOptions.Converters.Remove(this);

        JsonSerializer.Serialize(writer, value, defaultOptions);
    }
}
