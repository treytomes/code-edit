using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class PasteEvent : IBufferEvent
{
    private readonly string          _pasteText;
    private readonly TextRange?      _deletedRange;
    private readonly string?         _deletedText;

    internal CursorPosition At => _insertAt;
    private readonly CursorPosition  _insertAt;

    public PasteEvent(ITextBuffer buffer, IClipboardService clipboard)
    {
        if (!clipboard.TryGet(out var t)) t = string.Empty;
        _pasteText = t;

        if (buffer.Selection.HasValue)
        {
            var (s, e)    = CopyCommand.Normalise(buffer.Selection.Value);
            _deletedRange = new TextRange(s, e);
            _deletedText  = CopyCommand.SelectedText(buffer);
            _insertAt     = s;
        }
        else
        {
            _insertAt = buffer.Cursor;
        }
    }

    public void Execute(IMutableTextBuffer buf)
    {
        if (_deletedRange.HasValue)
        {
            buf.DeleteRange(_deletedRange.Value);
            buf.SetSelection(null);
        }
        buf.InsertText(_insertAt, _pasteText);
        buf.SetCursor(EndPosition(_insertAt, _pasteText));
        buf.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buf)
    {
        var pasteEnd = EndPosition(_insertAt, _pasteText);
        buf.DeleteRange(new TextRange(_insertAt, pasteEnd));

        if (_deletedRange.HasValue && _deletedText is not null)
        {
            buf.InsertText(_deletedRange.Value.Start, _deletedText);
            buf.SetCursor(_deletedRange.Value.End);
        }
        else
        {
            buf.SetCursor(_insertAt);
        }
        buf.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }

    public override string ToString() => $"at {_insertAt} len={_pasteText.Length}";

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
}
