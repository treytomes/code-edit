using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class IndentEvent : IBufferEvent
{
    private readonly IReadOnlyList<int> _lines;
    private readonly string             _indentString;
    private readonly bool               _dedent;
    // Captured during Execute for use in Undo: how many chars were removed per line.
    private readonly int[]              _removedLengths;

    public IndentEvent(IReadOnlyList<int> lines, string indentString, bool dedent)
    {
        _lines          = lines;
        _indentString   = indentString;
        _dedent         = dedent;
        _removedLengths = new int[lines.Count];
    }

    // Expose for EditorView selection adjustment after publish.
    public IReadOnlyList<int> Lines          => _lines;
    public string             IndentString   => _indentString;
    public bool               IsDedent       => _dedent;
    public IReadOnlyList<int> RemovedLengths => _removedLengths;

    public void Execute(IMutableTextBuffer buffer)
    {
        if (_dedent)
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                var lineIdx = _lines[i];
                if (lineIdx >= buffer.LineCount) continue;
                var text    = buffer.GetLine(lineIdx);
                var remove  = CountLeadingIndent(text, _indentString);
                _removedLengths[i] = remove;
                if (remove > 0)
                    buffer.DeleteRange(new TextRange(
                        new CursorPosition(lineIdx, 0),
                        new CursorPosition(lineIdx, remove)));
            }
        }
        else
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                var lineIdx = _lines[i];
                if (lineIdx >= buffer.LineCount) continue;
                buffer.InsertText(new CursorPosition(lineIdx, 0), _indentString);
            }
        }
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        if (_dedent)
        {
            // Re-insert the originally removed text at column 0 of each line.
            for (var i = 0; i < _lines.Count; i++)
            {
                var lineIdx = _lines[i];
                if (lineIdx >= buffer.LineCount) continue;
                var removed = _removedLengths[i];
                if (removed > 0)
                    buffer.InsertText(new CursorPosition(lineIdx, 0), _indentString[..removed]);
            }
        }
        else
        {
            // Remove the prepended indentString from column 0 of each line.
            var len = _indentString.Length;
            for (var i = 0; i < _lines.Count; i++)
            {
                var lineIdx = _lines[i];
                if (lineIdx >= buffer.LineCount) continue;
                var text = buffer.GetLine(lineIdx);
                var actual = Math.Min(len, text.Length);
                if (actual > 0)
                    buffer.DeleteRange(new TextRange(
                        new CursorPosition(lineIdx, 0),
                        new CursorPosition(lineIdx, actual)));
            }
        }
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }

    public override string ToString() =>
        $"{(_dedent ? "Dedent" : "Indent")} {_lines.Count} line(s) by \"{_indentString}\"";

    // Count how many leading characters of `text` match the indent prefix,
    // up to indentString.Length.
    private static int CountLeadingIndent(string text, string indentString)
    {
        var max = Math.Min(indentString.Length, text.Length);
        for (var i = 0; i < max; i++)
        {
            if (text[i] != indentString[i]) return i;
        }
        return max;
    }
}
