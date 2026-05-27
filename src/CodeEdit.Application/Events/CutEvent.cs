using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class CutEvent : IBufferEvent
{
    private readonly IClipboardService _clipboard;
    private readonly TextRange         _range;
    private readonly string            _text;

    public CutEvent(ITextBuffer buffer, IClipboardService clipboard)
    {
        _clipboard = clipboard;
        _range     = SelectionRange(buffer);
        _text      = buffer.Selection.HasValue
            ? CopyCommand.SelectedText(buffer)
            : CopyCommand.CurrentLine(buffer);
    }

    public void Execute(IMutableTextBuffer buf)
    {
        _clipboard.TrySet(_text);
        buf.DeleteRange(_range);
        buf.SetCursor(_range.Start);
        buf.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buf)
    {
        buf.InsertText(_range.Start, _text);
        buf.SetCursor(_range.End);
        buf.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }

    public override string ToString() => $"range={_range}";

    private static TextRange SelectionRange(ITextBuffer buffer)
    {
        if (buffer.Selection.HasValue)
        {
            var (s, e) = CopyCommand.Normalise(buffer.Selection.Value);
            return new TextRange(s, e);
        }

        var line    = buffer.Cursor.Line;
        var start   = new CursorPosition(line, 0);
        CursorPosition end;
        if (line + 1 < buffer.LineCount)
            end = new CursorPosition(line + 1, 0);
        else
            end = new CursorPosition(line, buffer.GetLine(line).Length);
        return new TextRange(start, end);
    }
}
