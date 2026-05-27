using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class InsertTextEvent : IBufferEvent
{
    public CursorPosition At           { get; }
    public string         Text         { get; private set; }

    // Non-null when a selection was deleted before the insert (typing over selection)
    private readonly TextRange?  _replacedRange;
    private readonly string?     _replacedText;

    public InsertTextEvent(CursorPosition at, string text,
                           TextRange? replacedRange = null, string? replacedText = null)
    {
        At             = at;
        Text           = text;
        _replacedRange = replacedRange;
        _replacedText  = replacedText;
    }

    public void Execute(IMutableTextBuffer buffer)
    {
        if (_replacedRange.HasValue)
        {
            buffer.DeleteRange(_replacedRange.Value);
            buffer.SetSelection(null);
        }
        buffer.InsertText(At, Text);
        buffer.SetCursor(EndPosition(At, Text));
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        var end = EndPosition(At, Text);
        buffer.DeleteRange(new TextRange(At, end));

        if (_replacedRange.HasValue && _replacedText is not null)
        {
            buffer.InsertText(_replacedRange.Value.Start, _replacedText);
            buffer.SetCursor(_replacedRange.Value.End);
        }
        else
        {
            buffer.SetCursor(At);
        }
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        // Never coalesce if this event replaced a selection
        if (_replacedRange.HasValue) return false;
        if (next is not InsertTextEvent other) return false;
        if (other._replacedRange.HasValue) return false;

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
