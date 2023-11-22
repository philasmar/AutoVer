namespace AutoVer.Exceptions;

public class InvalidProjectPathException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);