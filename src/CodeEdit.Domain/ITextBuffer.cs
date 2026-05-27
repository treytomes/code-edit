namespace CodeEdit.Domain;

public interface ITextBuffer
{
    int LineCount { get; }
    string GetLine(int lineIndex);
    CursorPosition Cursor { get; }
    Selection? Selection { get; }
    bool IsDirty { get; }
    string? FilePath { get; }
    string? DetectedLanguage { get; }
}
