using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Logging;

internal sealed class RollingFileLogger(string categoryName, RollingFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var shortCategory = categoryName.Contains('.')
            ? categoryName[(categoryName.LastIndexOf('.') + 1)..]
            : categoryName;

        var level = logLevel switch
        {
            LogLevel.Trace       => "TRC",
            LogLevel.Debug       => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning     => "WRN",
            LogLevel.Error       => "ERR",
            LogLevel.Critical    => "CRT",
            _                    => "???",
        };

        var message = formatter(state, exception);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var line = $"{timestamp} [{level}] {shortCategory,-20} {message}";

        if (exception is not null)
            line += Environment.NewLine + exception;

        provider.WriteLine(line);
    }
}
