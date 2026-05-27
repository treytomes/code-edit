namespace CodeEdit.Application.Ports;

public sealed class FileServiceException(string message, Exception? inner = null)
    : Exception(message, inner);
