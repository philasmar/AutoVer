using AutoVer.Models;

namespace AutoVer.Services;

public interface IChangeFileHandler
{
    ChangeFile GenerateChangeFile(UserConfiguration configuration, string? projectName, string? changeMessage);

    Task PersistChangeFile(UserConfiguration configuration, ChangeFile changeFile);

    Task<IList<ChangeFile>> LoadChangeFilesFromRepository(string repositoryRoot, string tagName);

    void ResetChangeFiles(UserConfiguration userConfiguration);
}