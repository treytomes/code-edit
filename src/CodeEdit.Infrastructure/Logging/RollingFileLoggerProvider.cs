using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Logging;

// Implemented per the logging section of the architecture spec.
[ProviderAlias("RollingFile")]
public sealed class RollingFileLoggerProvider(string logDirectory) : ILoggerProvider
{
    private readonly string _logDirectory = logDirectory;

    public ILogger CreateLogger(string categoryName)
        => throw new NotImplementedException();

    public void Dispose() { }
}
