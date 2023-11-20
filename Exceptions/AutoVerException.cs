namespace AutoVer.Exceptions;

public abstract class AutoVerException(string message, Exception? innerException = null)
    : Exception(message, innerException);