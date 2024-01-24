namespace AutoVer.Exceptions;

public class InvalidVersionTag(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);