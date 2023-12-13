namespace AutoVer.Exceptions;

public class InvalidProjectException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);