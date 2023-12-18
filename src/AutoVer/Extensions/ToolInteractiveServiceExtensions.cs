using AutoVer.Services;

namespace AutoVer.Extensions;

public static class ToolInteractiveServiceExtensions
{
    public static void WriteLine(this IToolInteractiveService service)
    {
        service.WriteLine(string.Empty);
    }
}