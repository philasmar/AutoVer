namespace AutoVer.Exceptions;

public class ConfiguredProjectNotFoundException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);