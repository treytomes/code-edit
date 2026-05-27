using CodeEdit.Domain;

namespace CodeEdit.Application.Ports;

public interface IFileService
{
    ITextBuffer Open(string path);

    /// <summary>Save an existing LazyFileBuffer back to its current path.</summary>
    void Save(ITextBuffer buffer);

    /// <summary>Write any buffer to a new path, then re-open it. Returns the new buffer.</summary>
    ITextBuffer SaveAs(ITextBuffer buffer, string path);
}
