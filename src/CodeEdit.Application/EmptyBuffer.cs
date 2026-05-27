using CodeEdit.Domain;

namespace CodeEdit.Application;

public sealed class EmptyBuffer : IMutableTextBuffer
{
    private readonly List<string> _lines = [""];
    private CursorPosition _cursor;
    private Selection?     _selection;
    private bool           _isDirty;

    public int            LineCount         => _lines.Count;
    public CursorPosition Cursor            => _cursor;
    public Selection?     Selection         => _selection;
    public bool           IsDirty          => _isDirty;
    public string?        FilePath         => null;
    public string?        DetectedLanguage => null;

    public string GetLine(int lineIndex) => _lines[lineIndex];

    public void InsertText(CursorPosition at, string text)
    {
        if (text.Length == 0) return;
        _isDirty = true;

        if (!text.Contains('\n'))
        {
            var line = _lines[at.Line];
            var col  = Math.Clamp(at.Column, 0, line.Length);
            _lines[at.Line] = line[..col] + text + line[col..];
            return;
        }

        var base_    = _lines[at.Line];
        var col2     = Math.Clamp(at.Column, 0, base_.Length);
        var segments = (base_[..col2] + text + base_[col2..]).Split('\n');
        _lines[at.Line] = segments[0];
        for (var i = 1; i < segments.Length; i++)
            _lines.Insert(at.Line + i, segments[i]);
    }

    public void DeleteRange(TextRange range)
    {
        var (start, end) = Normalise(range);
        _isDirty = true;

        if (start.Line == end.Line)
        {
            var line = _lines[start.Line];
            var s = Math.Clamp(start.Column, 0, line.Length);
            var e = Math.Clamp(end.Column, 0, line.Length);
            if (s >= e) return;
            _lines[start.Line] = line[..s] + line[e..];
            return;
        }

        var firstLine = _lines[start.Line];
        var lastLine  = _lines[end.Line];
        _lines[start.Line] = firstLine[..Math.Clamp(start.Column, 0, firstLine.Length)]
                           + lastLine[Math.Clamp(end.Column, 0, lastLine.Length)..];
        _lines.RemoveRange(start.Line + 1, end.Line - start.Line);
    }

    public void SetCursor(CursorPosition pos)
    {
        var line = Math.Clamp(pos.Line, 0, Math.Max(0, _lines.Count - 1));
        var col  = Math.Clamp(pos.Column, 0, _lines[line].Length);
        _cursor = new CursorPosition(line, col);
    }

    public void SetSelection(Selection? selection) => _selection = selection;

    public void ResizeCache(int terminalHeight) { }

    private static (CursorPosition start, CursorPosition end) Normalise(TextRange range)
    {
        var a = range.Start;
        var b = range.End;
        return a.Line < b.Line || (a.Line == b.Line && a.Column <= b.Column)
            ? (a, b) : (b, a);
    }
}
