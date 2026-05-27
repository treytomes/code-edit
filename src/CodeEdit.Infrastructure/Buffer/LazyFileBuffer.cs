using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Buffer;

// Implemented per the text-buffer feature spec.
public sealed class LazyFileBuffer : IMutableTextBuffer, IDisposable
{
    public int LineCount => throw new NotImplementedException();
    public string GetLine(int lineIndex) => throw new NotImplementedException();
    public CursorPosition Cursor => throw new NotImplementedException();
    public Selection? Selection => throw new NotImplementedException();
    public bool IsDirty => throw new NotImplementedException();
    public string? FilePath => throw new NotImplementedException();
    public string? DetectedLanguage => throw new NotImplementedException();

    public void InsertText(CursorPosition at, string text) => throw new NotImplementedException();
    public void DeleteRange(TextRange range) => throw new NotImplementedException();
    public void SetCursor(CursorPosition pos) => throw new NotImplementedException();
    public void SetSelection(Selection? selection) => throw new NotImplementedException();
    public void ResizeCache(int terminalHeight) => throw new NotImplementedException();

    public void Dispose() { }
}
