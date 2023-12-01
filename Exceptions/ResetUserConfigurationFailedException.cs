namespace AutoVer.Exceptions;

public class ResetUserConfigurationFailedException(string message, Exception? innerException = null)
    : AutoVerException(message, innerException);