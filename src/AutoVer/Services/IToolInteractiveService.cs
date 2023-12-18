namespace AutoVer.Services;

public interface IToolInteractiveService
{
    void WriteLine(string? message);
    void WriteErrorLine(string? message);
}