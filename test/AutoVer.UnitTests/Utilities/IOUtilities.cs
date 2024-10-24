using AutoVer.Constants;
using AutoVer.Models;
using System.Diagnostics;
using System.Xml;

namespace AutoVer.UnitTests.Utilities;

internal static class IOUtilities
{
    public static async Task<string> GetChangelog(string directory)
    {
        var changelogPath = Path.Combine(directory, "CHANGELOG.md");
        return await File.ReadAllTextAsync(changelogPath);
    }


    public static void RemoveReadOnly(string directory)
    {
        var dirInfo = new DirectoryInfo(directory);

        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
            }
        }
    }

    public static async Task<string> AddAutoVerFile(string directory, string content)
    {
        var autoVerFileDirectory = Path.Combine(directory, ".autover");
        if (!Directory.Exists(autoVerFileDirectory))
            Directory.CreateDirectory(autoVerFileDirectory);
        var autoVerFilePath = Path.Combine(autoVerFileDirectory, "autover.json");
        await File.WriteAllTextAsync(autoVerFilePath, content);

        return autoVerFilePath;
    }

    public static async Task<string> AddChangeFile(string projectName, IncrementType incrementType, string message, string savePath)
    {
        string changeFile =
$@"{{
    ""Projects"": [
        {{
            ""Name"": ""{projectName}"",
            ""Type"": ""{incrementType.ToString()}"",
            ""ChangelogMessages"": [
                ""{message}""
            ]
        }}
    ]
}}";
        var autoVerDirectory = Path.Combine(savePath, ".autover");
        if (!Directory.Exists(autoVerDirectory))
            Directory.CreateDirectory(autoVerDirectory);
        var changeDirectory = Path.Combine(autoVerDirectory, "changes");
        if (!Directory.Exists(changeDirectory))
            Directory.CreateDirectory(changeDirectory);
        var changeFilePath = Path.Combine(changeDirectory, $"{Guid.NewGuid().ToString().ToLower()}.json");
        await File.WriteAllTextAsync(changeFilePath, changeFile);

        return changeFilePath;
    }

    public static async Task AddUpdateFile(string path, string content)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Directory?.Exists ?? false)
            Directory.CreateDirectory(fileInfo.Directory!.FullName);
        await File.AppendAllTextAsync(path, content);
    }

    public static async Task<string> GetProjectVersion(string projectPath)
    {
        var extension = Path.GetExtension(projectPath);
        if (!string.Equals(extension, ".csproj") && !string.Equals(extension, ".nuspec"))
        {
            var errorMessage = $"Invalid project path {projectPath}. The project path must point to a .csproj or .nuspec file";
            throw new Exception(errorMessage);
        }

        var xmlProjectFile = new XmlDocument { PreserveWhitespace = true };
        xmlProjectFile.LoadXml(await File.ReadAllTextAsync(projectPath));

        var versionTag = ProjectConstants.VersionTag;
        if (string.Equals(extension, ".nuspec"))
            versionTag = ProjectConstants.NuspecVersionTag;
        var versionNode = xmlProjectFile.GetElementsByTagName(versionTag).Cast<XmlNode>().ToList();
        if (versionNode.Count > 0)
        {
            return versionNode.First().InnerText;
        }

        return string.Empty;
    }

    public static async Task SetProjectVersion(string projectPath, string version)
    {
        var extension = Path.GetExtension(projectPath);
        if (!string.Equals(extension, ".csproj") && !string.Equals(extension, ".nuspec"))
        {
            var errorMessage = $"Invalid project path {projectPath}. The project path must point to a .csproj or .nuspec file";
            throw new Exception(errorMessage);
        }

        var xmlProjectFile = new XmlDocument { PreserveWhitespace = true };
        xmlProjectFile.LoadXml(await File.ReadAllTextAsync(projectPath));

        var projectDefinition = new ProjectDefinition(
            xmlProjectFile,
            projectPath
        );

        var versionTag = ProjectConstants.VersionTag;
        if (string.Equals(extension, ".nuspec"))
            versionTag = ProjectConstants.NuspecVersionTag;
        var versionNode = xmlProjectFile.GetElementsByTagName(versionTag).Cast<XmlNode>().ToList();
        if (versionNode.Count > 0)
        {
            versionNode.First().InnerText = version;
        }
        else
        {
            var propertyGroupNode = xmlProjectFile.SelectSingleNode("//Project/PropertyGroup");

            if (propertyGroupNode == null)
            {
                XmlElement newPropertyGroup = xmlProjectFile.CreateElement("PropertyGroup");
                XmlElement versionElement = xmlProjectFile.CreateElement("Version");
                versionElement.InnerText = version;
                newPropertyGroup.AppendChild(versionElement);
                var projectNode = xmlProjectFile.SelectSingleNode("//Project");
                if (projectNode != null)
                {
                    projectNode.AppendChild(newPropertyGroup);
                }
            }
            else
            {
                XmlElement versionElement = xmlProjectFile.CreateElement("Version");
                versionElement.InnerText = version;
                propertyGroupNode.AppendChild(versionElement);
            }
        }

        xmlProjectFile.Save(projectDefinition.ProjectPath);
    }

    public static async Task<bool> CreateProject(params string[] path)
    {
        var outputDir = Path.Combine(path);
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);
        try
        {
            // Build the dotnet new command
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"new classlib -f net8.0",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            using (var process = new Process())
            {
                process.StartInfo = processStartInfo;
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                Console.WriteLine("Output:");
                Console.WriteLine(output);

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine("Error:");
                    Console.WriteLine(error);
                }

                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while running dotnet new: {ex.Message}");
            return false;
        }
    }

    public static bool AddGitignore(params string[] path)
    {
        var outputDir = Path.Combine(path);
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);
        try
        {
            // Build the dotnet new command
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"new gitignore",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = outputDir
            };

            using (var process = new Process())
            {
                process.StartInfo = processStartInfo;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                Console.WriteLine("Output:");
                Console.WriteLine(output);

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine("Error:");
                    Console.WriteLine(error);
                }

                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while running dotnet new gitignore: {ex.Message}");
            return false;
        }
    }
}
