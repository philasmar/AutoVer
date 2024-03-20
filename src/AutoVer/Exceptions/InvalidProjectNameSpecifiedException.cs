namespace AutoVer.Exceptions;

public class InvalidProjectNameSpecifiedException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);