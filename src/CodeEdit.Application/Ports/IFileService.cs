using CodeEdit.Domain;

namespace CodeEdit.Application.Ports;

public interface IFileService
{
    ITextBuffer Open(string path);
    void Save(ITextBuffer buffer, string path);
    void SaveNew(ITextBuffer buffer, string path);
}
