namespace AutoVer.Exceptions;

public class InvalidUserConfigurationException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);