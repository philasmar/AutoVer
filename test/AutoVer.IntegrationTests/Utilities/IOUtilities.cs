using AutoVer.Constants;
using AutoVer.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;

namespace AutoVer.IntegrationTests.Utilities;

internal static class IOUtilities
{
    public static async Task<string> GetChangelog(string directory)
    {
        var changelogPath = Path.Combine(directory, "CHANGELOG.md");
        return await File.ReadAllTextAsync(changelogPath);
    }

    public static bool ChangelogExists(string directory) =>
        File.Exists(Path.Combine(directory, "CHANGELOG.md"));

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

    public static bool AutoVerConfigExists(string directory) =>
        File.Exists(Path.Combine(directory, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ConfigFileName));

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

    public static int GetChangeFileCount(string repositoryRoot)
    {
        var changeDirectory = Path.Combine(repositoryRoot, ConfigurationConstants.ConfigFolderName, ConfigurationConstants.ChangesFolderName);
        if (!Directory.Exists(changeDirectory))
            return 0;
        return Directory.GetFiles(changeDirectory, "*.json").Length;
    }

    public static async Task<string> GetProjectVersion(string projectPath)
    {
        var xmlProjectFile = new XmlDocument { PreserveWhitespace = true };
        xmlProjectFile.LoadXml(await File.ReadAllTextAsync(projectPath));

        var versionNode = xmlProjectFile.GetElementsByTagName(ProjectConstants.VersionTag).Cast<XmlNode>().ToList();
        return versionNode.Count > 0 ? versionNode.First().InnerText : string.Empty;
    }

    public static async Task RemoveProjectVersionTag(string projectPath)
    {
        var xmlProjectFile = new XmlDocument { PreserveWhitespace = true };
        xmlProjectFile.LoadXml(await File.ReadAllTextAsync(projectPath));

        var versionNode = xmlProjectFile.GetElementsByTagName(ProjectConstants.VersionTag).Cast<XmlNode>().ToList();
        foreach (var node in versionNode)
        {
            node.ParentNode?.RemoveChild(node);
        }

        xmlProjectFile.Save(projectPath);
    }

    public static async Task SetProjectVersion(string projectPath, string version)
    {
        var xmlProjectFile = new XmlDocument { PreserveWhitespace = true };
        xmlProjectFile.LoadXml(await File.ReadAllTextAsync(projectPath));

        var versionNode = xmlProjectFile.GetElementsByTagName(ProjectConstants.VersionTag).Cast<XmlNode>().ToList();
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
                projectNode?.AppendChild(newPropertyGroup);
            }
            else
            {
                XmlElement versionElement = xmlProjectFile.CreateElement("Version");
                versionElement.InnerText = version;
                propertyGroupNode.AppendChild(versionElement);
            }
        }

        xmlProjectFile.Save(projectPath);
    }

    public static async Task<string> CreateDockerfile(string directory, string fileName = "Dockerfile")
    {
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(filePath, "FROM alpine:3.20\nCMD [\"true\"]\n");
        return filePath;
    }

    public static async Task SetDockerfileVersion(string dockerfilePath, string version)
    {
        var lines = (await File.ReadAllTextAsync(dockerfilePath)).Replace("\r\n", "\n").Split('\n').ToList();
        var labelLine = $"LABEL {ProjectConstants.DockerImageVersionLabel}=\"{version}\"";
        var index = lines.FindIndex(line => line.Contains(ProjectConstants.DockerImageVersionLabel));
        if (index >= 0)
            lines[index] = labelLine;
        else
            lines.Insert(1, labelLine);
        await File.WriteAllTextAsync(dockerfilePath, string.Join('\n', lines));
    }

    public static async Task<string> GetDockerfileVersion(string dockerfilePath)
    {
        var content = await File.ReadAllTextAsync(dockerfilePath);
        var match = Regex.Match(content, $"{Regex.Escape(ProjectConstants.DockerImageVersionLabel)}=\"?([^\"\\s]*)\"?");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    public static async Task RemoveDockerfileVersionLabel(string dockerfilePath)
    {
        var lines = (await File.ReadAllTextAsync(dockerfilePath)).Replace("\r\n", "\n").Split('\n').ToList();
        lines.RemoveAll(line => line.Contains(ProjectConstants.DockerImageVersionLabel));
        await File.WriteAllTextAsync(dockerfilePath, string.Join('\n', lines));
    }

    public static async Task<bool> CreateProject(params string[] path)
    {
        var outputDir = Path.Combine(path);
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var (exitCode, _, _) = await RunProcess("dotnet", "new classlib -f net8.0", outputDir);
        return exitCode == 0;
    }

    public static async Task<int> BuildProject(string projectPath)
    {
        var (exitCode, _, _) = await RunProcess("dotnet", $"build \"{projectPath}\"", Path.GetDirectoryName(projectPath)!);
        return exitCode;
    }

    public static bool AddGitignore(params string[] path)
    {
        var outputDir = Path.Combine(path);
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var (exitCode, _, _) = RunProcess("dotnet", "new gitignore", outputDir).GetAwaiter().GetResult();
        return exitCode == 0;
    }

    // Runs the actual built AutoVer CLI as a real subprocess with its own independent working
    // directory — required for testing relative --project-path behavior, since that depends on
    // the real process CWD (Environment.CurrentDirectory) and mutating that in-process would
    // leak across concurrently-running test bodies/hooks in this test process.
    public static async Task<(int ExitCode, string Output, string Error)> RunAutoVerCli(string workingDirectory, params string[] args)
    {
        var autoVerDllPath = Path.Combine(AppContext.BaseDirectory, "AutoVer.dll");
        var arguments = $"exec \"{autoVerDllPath}\" {string.Join(' ', args.Select(a => $"\"{a}\""))}";
        return await RunProcess("dotnet", arguments, workingDirectory);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        // Read stdout and stderr concurrently, not sequentially — if the child fills the
        // stderr pipe buffer before finishing stdout, awaiting stdout to completion first
        // deadlocks the child on its blocked stderr write.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();

        return (process.ExitCode, await outputTask, await errorTask);
    }
}
