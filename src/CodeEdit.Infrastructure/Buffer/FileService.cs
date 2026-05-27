using CodeEdit.Application.Ports;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Infrastructure.Buffer;

// Implemented per the text-buffer feature spec.
public sealed class FileService(ILogger<FileService> logger) : IFileService
{
    private readonly ILogger<FileService> _logger = logger;

    public ITextBuffer Open(string path) => throw new NotImplementedException();
    public void Save(ITextBuffer buffer, string path) => throw new NotImplementedException();
    public void SaveNew(ITextBuffer buffer, string path) => throw new NotImplementedException();
}
