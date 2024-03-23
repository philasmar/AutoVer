namespace AutoVer.Exceptions;

public class InvalidVersionTagException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);