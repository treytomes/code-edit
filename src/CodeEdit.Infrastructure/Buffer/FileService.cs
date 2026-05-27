using System.Text;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Buffer;

public sealed class FileService(ILogger<FileService> logger) : IFileService
{
    public ITextBuffer Open(string path)
    {
        try
        {
            var size = new FileInfo(path).Length;
            logger.LogInformation("Opening file: {Path} (size: {Size} bytes)", path, size);
            return LazyFileBuffer.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileServiceException($"Cannot open '{path}': {ex.Message}", ex);
        }
    }

    public void Save(ITextBuffer buffer)
    {
        if (buffer is not LazyFileBuffer lazy)
            throw new InvalidOperationException("Save requires an open file buffer. Use Save As to choose a path.");
        if (lazy.FilePath is null)
            throw new InvalidOperationException("Buffer has no file path. Use Save As.");

        logger.LogInformation("Saving file: {Path}", lazy.FilePath);
        try
        {
            lazy.SaveTo(lazy.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileServiceException($"Cannot save '{lazy.FilePath}': {ex.Message}", ex);
        }
    }

    public ITextBuffer SaveAs(ITextBuffer buffer, string path)
    {
        logger.LogInformation("Saving file as: {Path}", path);
        try
        {
            if (buffer is LazyFileBuffer lazy)
                lazy.SaveTo(path);
            else
                WriteAllLines(buffer, path);

            return Open(path);
        }
        catch (FileServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileServiceException($"Cannot save '{path}': {ex.Message}", ex);
        }
    }

    private static void WriteAllLines(ITextBuffer buffer, string path)
    {
        var tempPath  = path + ".tmp";
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using (var w = new StreamWriter(tempPath, append: false, utf8NoBom))
        {
            for (var i = 0; i < buffer.LineCount; i++)
            {
                if (i > 0) w.Write('\n');
                w.Write(buffer.GetLine(i));
            }
        }
        File.Replace(tempPath, path, null);
    }
}
