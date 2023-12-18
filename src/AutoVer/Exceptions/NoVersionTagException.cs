namespace AutoVer.Exceptions;

public class NoVersionTagException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);