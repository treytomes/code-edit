namespace CodeEdit.Domain;

public interface IMutableTextBuffer : ITextBuffer
{
    void InsertText(CursorPosition at, string text);
    void DeleteRange(TextRange range);
    void SetCursor(CursorPosition pos);
    void SetSelection(Selection? selection);
    void ResizeCache(int terminalHeight);
}
