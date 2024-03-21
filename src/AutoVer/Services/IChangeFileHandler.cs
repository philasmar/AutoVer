using AutoVer.Models;

namespace AutoVer.Services;

public interface IChangeFileHandler
{
    ChangeFile GenerateChangeFile(UserConfiguration configuration, IncrementType incrementType, string? projectName, string? changeMessage);

    Task PersistChangeFile(UserConfiguration configuration, ChangeFile changeFile);

    Task<IList<ChangeFile>> LoadChangeFilesFromRepository(string repositoryRoot, string? tagName = null);

    IDictionary<string, IncrementType> GetProjectIncrementTypesFromChangeFiles(IList<ChangeFile> changeFiles);

    void ResetChangeFiles(UserConfiguration userConfiguration);
}