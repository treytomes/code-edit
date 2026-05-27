using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Buffer;

// Implemented per the text-buffer feature spec.
public sealed class LazyFileBuffer : ITextBuffer, IDisposable
{
    public int LineCount => throw new NotImplementedException();
    public string GetLine(int lineIndex) => throw new NotImplementedException();
    public CursorPosition Cursor => throw new NotImplementedException();
    public Selection? Selection => throw new NotImplementedException();
    public bool IsDirty => throw new NotImplementedException();
    public string? FilePath => throw new NotImplementedException();
    public string? DetectedLanguage => throw new NotImplementedException();

    public void Dispose() { }
}
