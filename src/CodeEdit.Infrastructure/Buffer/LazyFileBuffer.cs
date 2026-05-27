using System.Text;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Buffer;

public sealed class LazyFileBuffer : IMutableTextBuffer, IDisposable
{
    private readonly string _filePath;
    private FileStream _fileStream;
    private long[] _physicalOffsets;
    private readonly List<LogicalLine> _logicalLines;
    private readonly LruCache<int, string> _lineCache;
    private CursorPosition _cursor;
    private Selection? _selection;
    private bool _hasStructuralEdits;

    private LazyFileBuffer(
        string filePath,
        FileStream fileStream,
        long[] physicalOffsets,
        List<LogicalLine> logicalLines,
        int terminalHeight)
    {
        _filePath = filePath;
        _fileStream = fileStream;
        _physicalOffsets = physicalOffsets;
        _logicalLines = logicalLines;
        _lineCache = new LruCache<int, string>(4 * Math.Max(terminalHeight, 1));
    }

    internal static LazyFileBuffer Open(string filePath)
    {
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (offsets, logicalLines) = ScanLines(fileStream);
        var terminalHeight = Console.WindowHeight;
        return new LazyFileBuffer(filePath, fileStream, offsets, logicalLines, terminalHeight);
    }

    // ── ITextBuffer ────────────────────────────────────────────────────────

    public int LineCount => _logicalLines.Count;

    public string GetLine(int lineIndex)
    {
        if ((uint)lineIndex >= (uint)_logicalLines.Count)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        var ll = _logicalLines[lineIndex];

        if (ll.IsEdited)
            return ll.EditedText;

        if (_lineCache.TryGet(lineIndex, out var cached))
            return cached;

        var line = ReadPhysicalLine(ll.PhysicalIndex);
        _lineCache.Put(lineIndex, line);
        return line;
    }

    public CursorPosition Cursor => _cursor;
    public Selection? Selection => _selection;
    public bool IsDirty => _hasStructuralEdits || _logicalLines.Any(l => l.IsEdited);
    public string? FilePath => _filePath;
    public string? DetectedLanguage => null;

    // ── IMutableTextBuffer ─────────────────────────────────────────────────

    public void InsertText(CursorPosition at, string text)
    {
        if (text.Length == 0) return;

        if (!text.Contains('\n'))
        {
            var current = GetLine(at.Line);
            var col = Math.Clamp(at.Column, 0, current.Length);
            var updated = current[..col] + text + current[col..];
            _logicalLines[at.Line] = new LogicalLine(_logicalLines[at.Line].PhysicalIndex, true, updated);
            _lineCache.Invalidate(at.Line);
            return;
        }

        // Multi-line insert: split existing line at column, interleave new segments
        var base_ = GetLine(at.Line);
        var col2 = Math.Clamp(at.Column, 0, base_.Length);
        var prefix = base_[..col2];
        var suffix = base_[col2..];

        var segments = (prefix + text + suffix).Split('\n');

        _logicalLines[at.Line] = new LogicalLine(-1, true, segments[0]);
        _lineCache.Invalidate(at.Line);

        for (var i = 1; i < segments.Length; i++)
            _logicalLines.Insert(at.Line + i, new LogicalLine(-1, true, segments[i]));

        // Invalidate cache entries for all lines that shifted up (same pattern as DeleteRange)
        for (var i = at.Line + segments.Length; i < _logicalLines.Count; i++)
            _lineCache.Invalidate(i);

        _hasStructuralEdits = true;
    }

    public void DeleteRange(TextRange range)
    {
        var (start, end) = Normalise(range);

        if (start.Line == end.Line)
        {
            var line = GetLine(start.Line);
            var s = Math.Clamp(start.Column, 0, line.Length);
            var e = Math.Clamp(end.Column, 0, line.Length);
            if (s >= e) return;

            var updated = line[..s] + line[e..];
            _logicalLines[start.Line] = new LogicalLine(_logicalLines[start.Line].PhysicalIndex, true, updated);
            _lineCache.Invalidate(start.Line);
            return;
        }

        // Multi-line delete: join first-line prefix with last-line suffix
        var firstLine = GetLine(start.Line);
        var lastLine  = GetLine(end.Line);
        var joined    = firstLine[..Math.Clamp(start.Column, 0, firstLine.Length)]
                      + lastLine[Math.Clamp(end.Column, 0, lastLine.Length)..];

        _logicalLines[start.Line] = new LogicalLine(_logicalLines[start.Line].PhysicalIndex, true, joined);
        _lineCache.Invalidate(start.Line);
        _logicalLines.RemoveRange(start.Line + 1, end.Line - start.Line);

        for (var i = start.Line + 1; i < _logicalLines.Count; i++)
            _lineCache.Invalidate(i);

        _hasStructuralEdits = true;
    }

    public void SetCursor(CursorPosition pos)
    {
        var line = Math.Clamp(pos.Line, 0, Math.Max(0, LineCount - 1));
        var col  = Math.Clamp(pos.Column, 0, GetLine(line).Length);
        _cursor = new CursorPosition(line, col);
    }

    public void SetSelection(Selection? selection) => _selection = selection;

    public void ResizeCache(int terminalHeight) =>
        _lineCache.Resize(4 * Math.Max(terminalHeight, 1));

    // ── Internal save API (called by FileService) ──────────────────────────

    internal void SaveTo(string targetPath)
    {
        var tempPath = targetPath + ".tmp";

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(tempStream, utf8NoBom, leaveOpen: true))
        {
            for (var i = 0; i < _logicalLines.Count; i++)
            {
                var ll = _logicalLines[i];
                if (ll.IsEdited || ll.PhysicalIndex == -1)
                {
                    writer.Write(ll.EditedText);
                }
                else
                {
                    writer.Flush();
                    CopyPhysicalLine(ll.PhysicalIndex, tempStream);
                }
                if (i < _logicalLines.Count - 1)
                    writer.Write('\n');
            }
        }

        File.Replace(tempPath, targetPath, null);
        RebuildAfterSave(targetPath);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private string ReadPhysicalLine(int physicalIndex)
    {
        _fileStream.Seek(_physicalOffsets[physicalIndex], SeekOrigin.Begin);
        var bytes = new List<byte>(128);

        int b;
        while ((b = _fileStream.ReadByte()) != -1 && b != '\n' && b != '\r')
            bytes.Add((byte)b);

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private void CopyPhysicalLine(int physicalIndex, Stream dest)
    {
        _fileStream.Seek(_physicalOffsets[physicalIndex], SeekOrigin.Begin);
        int b;
        while ((b = _fileStream.ReadByte()) != -1 && b != '\n' && b != '\r')
            dest.WriteByte((byte)b);
    }

    private void RebuildAfterSave(string path)
    {
        _fileStream.Dispose();
        _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (offsets, logicalLines) = ScanLines(_fileStream);
        _physicalOffsets = offsets;
        _logicalLines.Clear();
        _logicalLines.AddRange(logicalLines);
        _hasStructuralEdits = false;
        _lineCache.Clear();
    }

    private static (long[] offsets, List<LogicalLine> logicalLines) ScanLines(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        var offsets = new List<long> { 0L };

        int b;
        long pos = 0;
        while ((b = stream.ReadByte()) != -1)
        {
            pos++;
            if (b == '\r')
            {
                // Peek: consume \n if present (treat \r\n as single newline)
                var next = stream.ReadByte();
                if (next != -1 && next != '\n')
                {
                    // Standalone \r — push back the peeked byte by seeking back one
                    stream.Seek(-1, SeekOrigin.Current);
                }
                else if (next != -1)
                {
                    pos++; // consumed the \n
                }
                offsets.Add(pos);
            }
            else if (b == '\n')
            {
                offsets.Add(pos);
            }
        }

        // If the file ends with a newline the last offset points past EOF — drop it
        if (offsets.Count > 1 && offsets[^1] == stream.Length)
            offsets.RemoveAt(offsets.Count - 1);

        // Empty file: one empty line
        if (offsets.Count == 0)
            offsets.Add(0L);

        var offsetArr = offsets.ToArray();
        var logicalLines = new List<LogicalLine>(offsetArr.Length);
        for (var i = 0; i < offsetArr.Length; i++)
            logicalLines.Add(new LogicalLine(i, false, string.Empty));

        return (offsetArr, logicalLines);
    }

    private static (CursorPosition start, CursorPosition end) Normalise(TextRange range)
    {
        var a = range.Start;
        var b = range.End;
        if (a.Line < b.Line || (a.Line == b.Line && a.Column <= b.Column))
            return (a, b);
        return (b, a);
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        _fileStream.Dispose();
        _logicalLines.Clear();
        _lineCache.Clear();
    }
}
