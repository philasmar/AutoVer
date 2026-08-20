namespace AutoVer.Services.IO;

/// <summary>
/// The single shared "current directory" used to resolve relative paths across
/// IFileManager/IDirectoryManager/IPathManager. Centralizing it here means a caller sets it
/// once, in one place, instead of three managers having to be kept in lockstep by every call
/// site — the exact class of bug that kept resurfacing when they each tracked their own copy.
/// </summary>
public interface ICurrentDirectoryContext
{
    string CurrentDirectory { get; }

    void SetCurrentDirectory(string? currentDirectory);
}
