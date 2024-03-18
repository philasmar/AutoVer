using AutoVer.Models;

namespace AutoVer.Services;

public interface IChangeFileHandler
{
    ChangeFile GenerateChangeFile(UserConfiguration configuration, string? changeMessage);

    Task PersistChangeFile(UserConfiguration configuration, ChangeFile changeFile);

    Task<IList<ChangeFile>> LoadChangeFilesFromRepository(string repositoryRoot);

    void ResetChangeFiles(UserConfiguration userConfiguration);
}