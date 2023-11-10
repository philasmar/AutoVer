namespace AutoVer.Services;

public class ConsoleInteractiveService : IToolInteractiveService
{
    public ConsoleInteractiveService()
    {
        Console.Title = "AutoVer";
    }
    
    public void WriteLine(string? message)
    {
        Console.WriteLine(message);
    }
}