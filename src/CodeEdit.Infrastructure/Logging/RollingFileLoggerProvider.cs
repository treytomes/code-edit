using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Logging;

[ProviderAlias("RollingFile")]
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string _currentPath = string.Empty;

    public RollingFileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
        PurgeOldLogs();
    }

    public ILogger CreateLogger(string categoryName)
        => new RollingFileLogger(categoryName, this);

    internal void WriteLine(string line)
    {
        lock (_lock)
        {
            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
        }
    }

    private void EnsureWriter()
    {
        var todayPath = Path.Combine(
            _logDirectory,
            $"codeedit-{DateTime.UtcNow:yyyy-MM-dd}.log");

        if (_writer is not null && _currentPath == todayPath) return;

        _writer?.Dispose();
        _currentPath = todayPath;
        _writer = new StreamWriter(
            new FileStream(todayPath, FileMode.Append, FileAccess.Write, FileShare.Read));
    }

    private void PurgeOldLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-14);
        foreach (var file in Directory.EnumerateFiles(_logDirectory, "codeedit-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { /* best-effort */ }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
