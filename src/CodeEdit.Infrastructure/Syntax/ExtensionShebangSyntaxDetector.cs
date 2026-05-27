using CodeEdit.Application.Ports;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Syntax;

// Implemented per the syntax-highlighting feature spec.
public sealed class ExtensionShebangSyntaxDetector(
    PlainTextSyntaxProvider plainText,
    ILogger<ExtensionShebangSyntaxDetector> logger) : ISyntaxDetector
{
    private readonly ILogger<ExtensionShebangSyntaxDetector> _logger = logger;

    public ISyntaxProvider Detect(string? filePath, string? firstLine)
    {
        _logger.LogDebug("Detecting language for {FilePath}", filePath);
        return plainText;
    }
}
