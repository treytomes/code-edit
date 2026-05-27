using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class InsertTextEvent(CursorPosition at, string text) : IBufferEvent
{
    public CursorPosition At   { get; }          = at;
    public string         Text { get; private set; } = text;

    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.InsertText(At, Text);
        buffer.SetCursor(EndPosition(At, Text));
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        var end = EndPosition(At, Text);
        buffer.DeleteRange(new TextRange(At, end));
        buffer.SetCursor(At);
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        if (next is not InsertTextEvent other) return false;

        var thisEnd = EndPosition(At, Text);
        if (other.At != thisEnd) return false;

        if (IsWordBreak(Text[^1]) && IsWordBreak(other.Text[0])) return false;

        merged = new InsertTextEvent(At, Text + other.Text);
        return true;
    }

    public override string ToString() => $"at {At} text={Text.Length}ch";

    private static CursorPosition EndPosition(CursorPosition start, string text)
    {
        var line = start.Line;
        var col  = start.Column;
        foreach (var ch in text)
        {
            if (ch == '\n') { line++; col = 0; }
            else            { col++; }
        }
        return new CursorPosition(line, col);
    }

    private static bool IsWordBreak(char c) =>
        c is ' ' or '\t' or '\n' or '\r'
            or '.' or ',' or ';' or ':' or '!' or '?'
            or '(' or ')' or '[' or ']' or '{' or '}'
            or '<' or '>' or '/' or '\\' or '-' or '_'
            or '"' or '\'';
}
