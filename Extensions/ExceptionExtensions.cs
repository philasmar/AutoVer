using AutoVer.Exceptions;

namespace AutoVer.Extensions;

public static class ExceptionExtensions
{
    /// <summary>
    /// True if the <paramref name="e"/> inherits from
    /// <see cref="AutoVerException"/>.
    /// </summary>
    public static bool IsExpectedException(this Exception e) =>
        e is AutoVerException;

    public static string PrettyPrint(this Exception? e)
    {
        if (null == e)
            return string.Empty;

        return $"{Environment.NewLine}{e.Message}{Environment.NewLine}{e.StackTrace}{PrettyPrint(e.InnerException)}";
    }
}