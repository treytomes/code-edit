using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Tests.Application;

public sealed class ClipboardCommandTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static EmptyBuffer BufferWithText(string text)
    {
        var buf = new EmptyBuffer();
        buf.InsertText(new CursorPosition(0, 0), text);
        buf.SetCursor(new CursorPosition(0, 0));
        return buf;
    }

    private static EmptyBuffer MultiLineBuffer()
    {
        var buf = new EmptyBuffer();
        buf.InsertText(new CursorPosition(0, 0), "hello\nworld\nfoo");
        buf.SetCursor(new CursorPosition(0, 0));
        return buf;
    }

    // ── CopyCommand ────────────────────────────────────────────────────────

    [Fact]
    public void Copy_WithSelection_ClipboardContainsSelectedText()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard();
        buf.SetSelection(new Selection(new CursorPosition(0, 0), new CursorPosition(0, 5)));

        var result = CopyCommand.Execute(buf, clip);

        Assert.True(result);
        Assert.Equal("hello", clip.Contents);
    }

    [Fact]
    public void Copy_WithNoSelection_ReturnsFalseAndClipboardUnchanged()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard { Contents = "old" };

        var result = CopyCommand.Execute(buf, clip);

        Assert.False(result);
        Assert.Equal("old", clip.Contents);
    }

    [Fact]
    public void Copy_DoesNotModifyBuffer()
    {
        var buf  = BufferWithText("hello");
        var clip = new FakeClipboard();
        buf.SetSelection(new Selection(new CursorPosition(0, 0), new CursorPosition(0, 5)));

        CopyCommand.Execute(buf, clip);

        Assert.Equal("hello", buf.GetLine(0));
    }

    [Fact]
    public void Copy_MultiLineSelection_ClipboardContainsLinesJoinedWithNewline()
    {
        var buf  = MultiLineBuffer();
        var clip = new FakeClipboard();
        buf.SetSelection(new Selection(new CursorPosition(0, 2), new CursorPosition(1, 3)));

        CopyCommand.Execute(buf, clip);

        Assert.Equal("llo\nwor", clip.Contents);
    }

    // ── CutEvent ──────────────────────────────────────────────────────────

    [Fact]
    public void Cut_WithSelection_ClipboardHasSelectionTextAndSelectionRemovedFromBuffer()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard();
        buf.SetSelection(new Selection(new CursorPosition(0, 6), new CursorPosition(0, 11)));

        var ev = new CutEvent(buf, clip);
        ev.Execute(buf);

        Assert.Equal("world", clip.Contents);
        Assert.Equal("hello ", buf.GetLine(0));
        Assert.Equal(new CursorPosition(0, 6), buf.Cursor);
    }

    [Fact]
    public void Cut_WithNoSelection_ClipboardHasFullLineAndLineRemovedFromBuffer()
    {
        var buf  = MultiLineBuffer();
        var clip = new FakeClipboard();
        buf.SetCursor(new CursorPosition(1, 2));

        var ev = new CutEvent(buf, clip);
        ev.Execute(buf);

        Assert.Equal("world\n", clip.Contents);
        Assert.Equal(2, buf.LineCount);
        Assert.Equal("hello", buf.GetLine(0));
        Assert.Equal("foo", buf.GetLine(1));
    }

    [Fact]
    public void Cut_Undo_RemovedTextRestoredAndCursorAtEndOfRestoredText()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard();
        buf.SetSelection(new Selection(new CursorPosition(0, 6), new CursorPosition(0, 11)));

        var ev = new CutEvent(buf, clip);
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Equal("hello world", buf.GetLine(0));
        Assert.Equal(new CursorPosition(0, 11), buf.Cursor);
    }

    // ── PasteEvent ────────────────────────────────────────────────────────

    [Fact]
    public void Paste_WithNoSelection_TextInsertedAtCursorAndCursorAtEndOfPaste()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard { Contents = "dear " };
        buf.SetCursor(new CursorPosition(0, 6));

        var ev = new PasteEvent(buf, clip);
        ev.Execute(buf);

        Assert.Equal("hello dear world", buf.GetLine(0));
        Assert.Equal(new CursorPosition(0, 11), buf.Cursor);
    }

    [Fact]
    public void Paste_WithSelection_SelectionDeletedAndClipboardInsertedAtSelectionStart()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard { Contents = "there" };
        buf.SetSelection(new Selection(new CursorPosition(0, 6), new CursorPosition(0, 11)));

        var ev = new PasteEvent(buf, clip);
        ev.Execute(buf);

        Assert.Equal("hello there", buf.GetLine(0));
        Assert.Equal(new CursorPosition(0, 11), buf.Cursor);
    }

    [Fact]
    public void Paste_UndoNoSelection_PastedTextRemovedAndCursorAtOriginalPosition()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard { Contents = "dear " };
        buf.SetCursor(new CursorPosition(0, 6));

        var ev = new PasteEvent(buf, clip);
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Equal("hello world", buf.GetLine(0));
        Assert.Equal(new CursorPosition(0, 6), buf.Cursor);
    }

    [Fact]
    public void Paste_UndoWithSelection_PastedTextRemovedAndSelectionTextRestored()
    {
        var buf  = BufferWithText("hello world");
        var clip = new FakeClipboard { Contents = "there" };
        buf.SetSelection(new Selection(new CursorPosition(0, 6), new CursorPosition(0, 11)));

        var ev = new PasteEvent(buf, clip);
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Equal("hello world", buf.GetLine(0));
    }

    // ── FakeClipboard ──────────────────────────────────────────────────────

    private sealed class FakeClipboard : IClipboardService
    {
        public string Contents { get; set; } = string.Empty;
        public bool IsSupported => true;
        public bool TryGet(out string text) { text = Contents; return true; }
        public bool TrySet(string text) { Contents = text; return true; }
    }
}
