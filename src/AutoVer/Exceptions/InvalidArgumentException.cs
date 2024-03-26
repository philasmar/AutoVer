namespace AutoVer.Exceptions;

public class InvalidArgumentException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);