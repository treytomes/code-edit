using CodeEdit.Application.Events;
using CodeEdit.Domain;

namespace CodeEdit.Tests.Application;

public sealed class IndentEventTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static RealBuffer MakeBuffer(params string[] lines)
    {
        var buf = new RealBuffer();
        foreach (var l in lines) buf.Lines.Add(l);
        return buf;
    }

    // A buffer that actually mutates its Lines list.
    private sealed class RealBuffer : IMutableTextBuffer
    {
        public List<string> Lines { get; } = [];

        public int            LineCount         => Lines.Count;
        public CursorPosition Cursor            { get; private set; }
        public Selection?     Selection         { get; private set; }
        public bool           IsDirty           => false;
        public string?        FilePath          => null;
        public string?        DetectedLanguage  => null;

        public string GetLine(int i) => i < Lines.Count ? Lines[i] : string.Empty;

        public void InsertText(CursorPosition at, string text)
        {
            while (Lines.Count <= at.Line) Lines.Add("");
            var line = Lines[at.Line];
            Lines[at.Line] = line[..at.Column] + text + line[at.Column..];
        }

        public void DeleteRange(TextRange range)
        {
            if (range.Start.Line == range.End.Line)
            {
                var line = Lines[range.Start.Line];
                Lines[range.Start.Line] = line[..range.Start.Column] + line[range.End.Column..];
            }
        }

        public void SetCursor(CursorPosition pos)    { Cursor = pos; }
        public void SetSelection(Selection? sel)     { Selection = sel; }
        public void ResizeCache(int terminalHeight)  { }
    }

    // ── Indent (spaces) ────────────────────────────────────────────────────

    [Fact]
    public void Execute_Indent_PrependsIndentString()
    {
        var buf = MakeBuffer("hello");
        var ev  = new IndentEvent([0], "    ", dedent: false);

        ev.Execute(buf);

        Assert.Equal("    hello", buf.Lines[0]);
    }

    [Fact]
    public void Execute_Indent_MultipleLines()
    {
        var buf = MakeBuffer("foo", "bar");
        var ev  = new IndentEvent([0, 1], "  ", dedent: false);

        ev.Execute(buf);

        Assert.Equal("  foo", buf.Lines[0]);
        Assert.Equal("  bar", buf.Lines[1]);
    }

    [Fact]
    public void Undo_Indent_RemovesPrependedChars()
    {
        var buf = MakeBuffer("hello");
        var ev  = new IndentEvent([0], "    ", dedent: false);
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Equal("hello", buf.Lines[0]);
    }

    // ── Dedent (spaces) ────────────────────────────────────────────────────

    [Fact]
    public void Execute_Dedent_RemovesFullIndent()
    {
        var buf = MakeBuffer("    hello");
        var ev  = new IndentEvent([0], "    ", dedent: true);
        ev.Execute(buf);

        Assert.Equal("hello", buf.Lines[0]);
        Assert.Equal(4, ev.RemovedLengths[0]);
    }

    [Fact]
    public void Execute_Dedent_PartialIndentRemovedUpToAvailable()
    {
        var buf = MakeBuffer("  hello");
        var ev  = new IndentEvent([0], "    ", dedent: true);
        ev.Execute(buf);

        Assert.Equal("hello", buf.Lines[0]);
        Assert.Equal(2, ev.RemovedLengths[0]);
    }

    [Fact]
    public void Execute_Dedent_NoIndentLeavesLineUnchanged()
    {
        var buf = MakeBuffer("hello");
        var ev  = new IndentEvent([0], "    ", dedent: true);
        ev.Execute(buf);

        Assert.Equal("hello", buf.Lines[0]);
        Assert.Equal(0, ev.RemovedLengths[0]);
    }

    [Fact]
    public void Undo_Dedent_RestoresRemovedChars()
    {
        var buf = MakeBuffer("    hello");
        var ev  = new IndentEvent([0], "    ", dedent: true);
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Equal("    hello", buf.Lines[0]);
    }

    [Fact]
    public void Undo_Dedent_PartialRestoresOnlyRemovedChars()
    {
        var buf = MakeBuffer("  hello");
        var ev  = new IndentEvent([0], "    ", dedent: true);
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Equal("  hello", buf.Lines[0]);
    }

    // ── Tab character indent ───────────────────────────────────────────────

    [Fact]
    public void Execute_Indent_TabCharacter()
    {
        var buf = MakeBuffer("hello");
        var ev  = new IndentEvent([0], "\t", dedent: false);
        ev.Execute(buf);

        Assert.Equal("\thello", buf.Lines[0]);
    }

    [Fact]
    public void Execute_Dedent_TabCharacter()
    {
        var buf = MakeBuffer("\thello");
        var ev  = new IndentEvent([0], "\t", dedent: true);
        ev.Execute(buf);

        Assert.Equal("hello", buf.Lines[0]);
        Assert.Equal(1, ev.RemovedLengths[0]);
    }

    // ── TryCoalesce ────────────────────────────────────────────────────────

    [Fact]
    public void TryCoalesce_AlwaysReturnsFalse()
    {
        var ev1 = new IndentEvent([0], "    ", dedent: false);
        var ev2 = new IndentEvent([0], "    ", dedent: false);

        Assert.False(ev1.TryCoalesce(ev2, out _));
    }

    // ── Line out of range ──────────────────────────────────────────────────

    [Fact]
    public void Execute_LineOutOfRange_IsSkipped()
    {
        var buf = MakeBuffer("hello");
        var ev  = new IndentEvent([5], "    ", dedent: false);

        var ex = Record.Exception(() => ev.Execute(buf));
        Assert.Null(ex);
        Assert.Equal("hello", buf.Lines[0]);
    }
}
