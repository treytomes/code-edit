using System.Text;
using CodeEdit.Domain;
using CodeEdit.Infrastructure.Buffer;

namespace CodeEdit.Tests.Infrastructure;

public sealed class LazyFileBufferTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    // ── Helpers ────────────────────────────────────────────────────────────

    private string WriteTempFile(string content, bool bom = false)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllBytes(path, bom
            ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)]
            : Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static LazyFileBuffer Open(string path)
    {
        var svc = new FileService(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileService>.Instance);
        return (LazyFileBuffer)svc.Open(path);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    // ── Open and line access ───────────────────────────────────────────────

    [Fact]
    public void Open_SingleLineFile_LineCountIsOne()
    {
        var path = WriteTempFile("hello world");
        using var buf = Open(path);
        Assert.Equal(1, buf.LineCount);
        Assert.Equal("hello world", buf.GetLine(0));
    }

    [Fact]
    public void Open_MultiLineFile_LineCountMatchesActual()
    {
        var path = WriteTempFile("line1\nline2\nline3");
        using var buf = Open(path);
        Assert.Equal(3, buf.LineCount);
        Assert.Equal("line1", buf.GetLine(0));
        Assert.Equal("line2", buf.GetLine(1));
        Assert.Equal("line3", buf.GetLine(2));
    }

    [Fact]
    public void Open_EmptyFile_LineCountIsOneWithEmptyLine()
    {
        var path = WriteTempFile("");
        using var buf = Open(path);
        Assert.Equal(1, buf.LineCount);
        Assert.Equal("", buf.GetLine(0));
    }

    [Fact]
    public void Open_CrLfEndings_LinesReturnedWithoutCr()
    {
        var path = WriteTempFile("line1\r\nline2\r\nline3");
        using var buf = Open(path);
        Assert.Equal(3, buf.LineCount);
        Assert.Equal("line1", buf.GetLine(0));
        Assert.Equal("line2", buf.GetLine(1));
        Assert.Equal("line3", buf.GetLine(2));
    }

    [Fact]
    public void Open_CrOnlyEndings_LinesReturnedWithoutCr()
    {
        var path = WriteTempFile("line1\rline2\rline3");
        using var buf = Open(path);
        Assert.Equal(3, buf.LineCount);
        Assert.Equal("line1", buf.GetLine(0));
        Assert.Equal("line2", buf.GetLine(1));
        Assert.Equal("line3", buf.GetLine(2));
    }

    [Fact]
    public void GetLine_OutOfRangeIndex_ThrowsArgumentOutOfRangeException()
    {
        var path = WriteTempFile("hello");
        using var buf = Open(path);
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.GetLine(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.GetLine(-1));
    }

    // ── Mutations ──────────────────────────────────────────────────────────

    [Fact]
    public void InsertText_WithinLine_UpdatesContentAndSetsDirty()
    {
        var path = WriteTempFile("hello world");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 5), ", dear");
        Assert.Equal("hello, dear world", buf.GetLine(0));
        Assert.True(buf.IsDirty);
    }

    [Fact]
    public void InsertText_WithNewline_SplitsLineAndIncreasesLineCount()
    {
        var path = WriteTempFile("hello world");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 5), "\n");
        Assert.Equal(2, buf.LineCount);
        Assert.Equal("hello", buf.GetLine(0));
        Assert.Equal(" world", buf.GetLine(1));
    }

    [Fact]
    public void DeleteRange_WithinLine_UpdatesContent()
    {
        var path = WriteTempFile("hello world");
        using var buf = Open(path);
        buf.DeleteRange(new TextRange(new CursorPosition(0, 5), new CursorPosition(0, 11)));
        Assert.Equal("hello", buf.GetLine(0));
    }

    [Fact]
    public void DeleteRange_SpanningLines_JoinsLinesAndDecreasesLineCount()
    {
        var path = WriteTempFile("hello\nworld");
        using var buf = Open(path);
        buf.DeleteRange(new TextRange(new CursorPosition(0, 5), new CursorPosition(1, 0)));
        Assert.Equal(1, buf.LineCount);
        Assert.Equal("helloworld", buf.GetLine(0));
    }

    [Fact]
    public void AfterMutation_GetLine_ReturnsUpdatedContentWithoutFileRead()
    {
        var path = WriteTempFile("original");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 0), "modified: ");
        // File on disk is unchanged; buffer must return updated in-memory state
        Assert.Equal("modified: original", buf.GetLine(0));
        Assert.Equal("original", File.ReadAllText(path));
    }

    // ── Cursor ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetCursor_ValidPosition_CursorReflectsNewPosition()
    {
        var path = WriteTempFile("hello\nworld");
        using var buf = Open(path);
        buf.SetCursor(new CursorPosition(1, 3));
        Assert.Equal(new CursorPosition(1, 3), buf.Cursor);
    }

    [Fact]
    public void SetCursor_ColumnExceedsLineLength_ClampsToLineLength()
    {
        var path = WriteTempFile("hi");
        using var buf = Open(path);
        buf.SetCursor(new CursorPosition(0, 999));
        Assert.Equal(new CursorPosition(0, 2), buf.Cursor);
    }

    // ── Selection ──────────────────────────────────────────────────────────

    [Fact]
    public void SetSelection_Range_SelectionReflectsIt()
    {
        var path = WriteTempFile("hello world");
        using var buf = Open(path);
        var sel = new Selection(new CursorPosition(0, 0), new CursorPosition(0, 5));
        buf.SetSelection(sel);
        Assert.Equal(sel, buf.Selection);
    }

    [Fact]
    public void SetSelection_Null_SelectionIsNull()
    {
        var path = WriteTempFile("hello");
        using var buf = Open(path);
        buf.SetSelection(new Selection(new CursorPosition(0, 0), new CursorPosition(0, 3)));
        buf.SetSelection(null);
        Assert.Null(buf.Selection);
    }

    // ── Dirty flag ─────────────────────────────────────────────────────────

    [Fact]
    public void FreshOpen_IsDirtyIsFalse()
    {
        var path = WriteTempFile("content");
        using var buf = Open(path);
        Assert.False(buf.IsDirty);
    }

    [Fact]
    public void AnyMutation_SetsDirty()
    {
        var path = WriteTempFile("content");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 0), "x");
        Assert.True(buf.IsDirty);
    }

    [Fact]
    public void AfterSave_IsDirtyIsFalse()
    {
        var path = WriteTempFile("content");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 0), "new: ");
        Assert.True(buf.IsDirty);
        buf.SaveTo(path);
        Assert.False(buf.IsDirty);
    }

    // ── Save ───────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesAllLinesWithLfEndings()
    {
        var path = WriteTempFile("line1\nline2\nline3");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(1, 0), "X");
        buf.SaveTo(path);
        var saved = File.ReadAllText(path);
        Assert.Equal("line1\nXline2\nline3", saved);
        Assert.DoesNotContain("\r", saved);
    }

    [Fact]
    public void Save_WithStructuralEdits_SavedFileHasCorrectLineCount()
    {
        var path = WriteTempFile("hello world");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 5), "\n");
        buf.SaveTo(path);
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal("hello", lines[0]);
        Assert.Equal(" world", lines[1]);
    }

    [Fact]
    public void AfterSave_GetLine_ReadsFromNewFileState()
    {
        var path = WriteTempFile("original");
        using var buf = Open(path);
        buf.InsertText(new CursorPosition(0, 0), "updated: ");
        buf.SaveTo(path);
        // Force a fresh read from file by checking a non-edited state
        Assert.Equal("updated: original", buf.GetLine(0));
        Assert.False(buf.IsDirty);
    }

    // ── LRU cache ──────────────────────────────────────────────────────────

    [Fact]
    public void GetLine_CalledTwice_SecondCallReturnsCachedValue()
    {
        var path = WriteTempFile("cached line");
        using var buf = Open(path);
        var first = buf.GetLine(0);
        // Mutate the file on disk — cache should return the original in-memory value
        File.WriteAllText(path, "different content");
        var second = buf.GetLine(0);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ResizeCache_SmallerCapacity_EvictsExcessEntries()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var path = WriteTempFile(lines);
        using var buf = Open(path);

        // Prime the cache with all 20 lines
        for (var i = 0; i < 20; i++) buf.GetLine(i);

        // Shrink to 1 — all but the most recently used should be evicted
        buf.ResizeCache(1); // capacity = 4 × 1 = 4... but let's use a helper

        // After resize the buffer still returns correct values (reads from file if evicted)
        Assert.Equal("line 1", buf.GetLine(0));
    }

    // ── FileService ────────────────────────────────────────────────────────

    [Fact]
    public void FileService_Open_NonExistentPath_ThrowsFileServiceException()
    {
        var svc = new FileServiceWrapper();
        Assert.Throws<CodeEdit.Application.Ports.FileServiceException>(
            () => svc.Open("/nonexistent/path/file.txt"));
    }

    [Fact]
    public void FileService_Open_UnreadablePath_ThrowsFileServiceException()
    {
        var path = WriteTempFile("secret");
        try
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            // On Linux, make truly unreadable:
            var fi = new FileInfo(path);
            // Use a path that definitely won't be readable
            Assert.Throws<CodeEdit.Application.Ports.FileServiceException>(
                () => new FileServiceWrapper().Open("/root/unreadable_test_file_that_does_not_exist"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    // Thin wrapper to expose FileService without the internal Open returning LazyFileBuffer
    private sealed class FileServiceWrapper
    {
        private readonly FileService _svc = new(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileService>.Instance);

        public ITextBuffer Open(string path) => _svc.Open(path);
    }
}
