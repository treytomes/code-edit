using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Buffer;

public sealed class FileService(ILogger<FileService> logger) : IFileService
{
    private readonly ILogger<FileService> _logger = logger;

    public ITextBuffer Open(string path)
    {
        try
        {
            var size = new FileInfo(path).Length;
            _logger.LogInformation("Opening file: {Path} (size: {Size} bytes)", path, size);
            return LazyFileBuffer.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileServiceException($"Cannot open '{path}': {ex.Message}", ex);
        }
    }

    public void Save(ITextBuffer buffer, string path) => SaveCore(buffer, path);

    public void SaveNew(ITextBuffer buffer, string path) => SaveCore(buffer, path);

    private void SaveCore(ITextBuffer buffer, string path)
    {
        if (buffer is not LazyFileBuffer lazy)
            throw new InvalidOperationException("Save requires a LazyFileBuffer instance.");

        _logger.LogInformation("Saving file: {Path}", path);
        try
        {
            lazy.SaveTo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileServiceException($"Cannot save '{path}': {ex.Message}", ex);
        }
    }
}
