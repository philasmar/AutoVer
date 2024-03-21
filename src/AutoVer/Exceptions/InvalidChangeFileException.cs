namespace AutoVer.Exceptions;

public class InvalidChangeFileException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);