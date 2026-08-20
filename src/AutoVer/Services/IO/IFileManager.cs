namespace AutoVer.Services.IO;

public interface IFileManager
{
    bool Exists(string path);
    string ReadAllText(string path);
    Task<string> ReadAllTextAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task AppendAllTextAsync(string path, string? contents);
    void WriteAllText(string path, string? contents);
    Task WriteAllTextAsync(string path, string? contents);

    /// <summary>
    /// Opens (creating/truncating) the file for writing raw bytes, e.g. for APIs like
    /// XmlDocument.Save(Stream) that need a Stream rather than a string of content.
    /// </summary>
    Stream OpenWrite(string path);
    void Delete(string path);
}