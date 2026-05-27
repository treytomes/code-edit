using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Application;

public sealed class EventBusTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static EventBus CreateBus() =>
        new(NullLogger<EventBus>.Instance);

    private static FakeBuffer CreateBuffer(string singleLine = "hello world")
    {
        var buf = new FakeBuffer();
        buf.Lines.Add(singleLine);
        return buf;
    }

    private static EventBus BusWithBuffer(out FakeBuffer buf)
    {
        var bus = CreateBus();
        buf = CreateBuffer();
        bus.SetBuffer(buf);
        return bus;
    }

    // ── Publish ────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_Event_ExecuteCalledAndEventExecutedFires()
    {
        var bus = BusWithBuffer(out var buf);
        var ev = new InsertTextEvent(new CursorPosition(0, 0), "x");

        IBufferEvent? fired = null;
        bus.EventExecuted += (_, a) => fired = a.BufferEvent;

        bus.Publish(ev);

        Assert.True(buf.InsertCalled);
        Assert.NotNull(fired);
    }

    [Fact]
    public void Publish_Event_AppearsOnUndoStack()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "x"));
        Assert.True(bus.CanUndo);
    }

    [Fact]
    public void Publish_Event_ClearsRedoStack()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "x"));
        bus.Undo();
        Assert.True(bus.CanRedo);

        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "y"));
        Assert.False(bus.CanRedo);
    }

    // ── Undo / Redo ────────────────────────────────────────────────────────

    [Fact]
    public void Undo_WithEvent_UndoCalledEventUndoneFiresAndCanRedoIsTrue()
    {
        var bus = BusWithBuffer(out var buf);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "x"));
        buf.Reset();

        IBufferEvent? fired = null;
        bus.EventUndone += (_, a) => fired = a.BufferEvent;

        bus.Undo();

        Assert.True(buf.DeleteCalled);
        Assert.NotNull(fired);
        Assert.True(bus.CanRedo);
    }

    [Fact]
    public void Undo_EmptyStack_NoExceptionAndEventUndoneDoesNotFire()
    {
        var bus = BusWithBuffer(out _);
        var fired = false;
        bus.EventUndone += (_, _) => fired = true;

        var ex = Record.Exception(() => bus.Undo());

        Assert.Null(ex);
        Assert.False(fired);
    }

    [Fact]
    public void Redo_AfterUndo_ExecuteCalledAgainAndEventRedoneFiresAndCanUndoIsTrue()
    {
        var bus = BusWithBuffer(out var buf);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "x"));
        bus.Undo();
        buf.Reset();

        IBufferEvent? fired = null;
        bus.EventRedone += (_, a) => fired = a.BufferEvent;

        bus.Redo();

        Assert.True(buf.InsertCalled);
        Assert.NotNull(fired);
        Assert.True(bus.CanUndo);
    }

    [Fact]
    public void Redo_EmptyStack_NoExceptionAndEventRedoneDoesNotFire()
    {
        var bus = BusWithBuffer(out _);
        var fired = false;
        bus.EventRedone += (_, _) => fired = true;

        var ex = Record.Exception(() => bus.Redo());

        Assert.Null(ex);
        Assert.False(fired);
    }

    [Fact]
    public void Publish_AfterUndo_ClearsRedoStack()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "x"));
        bus.Undo();
        Assert.True(bus.CanRedo);

        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "y"));
        Assert.False(bus.CanRedo);
    }

    // ── Coalescing (InsertTextEvent) ───────────────────────────────────────

    [Fact]
    public void Coalescing_TwoAdjacentSingleCharInsertions_OneUndoStep()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "h"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 1), "i"));

        bus.Undo();
        Assert.False(bus.CanUndo);
    }

    [Fact]
    public void Coalescing_NonAdjacentInsertions_SeparateUndoSteps()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "a"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 5), "b")); // gap — not adjacent

        bus.Undo();
        Assert.True(bus.CanUndo);
    }

    [Fact]
    public void Coalescing_TwoConsecutiveWordBreaks_SeparateUndoSteps()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), " "));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 1), " "));

        bus.Undo();
        Assert.True(bus.CanUndo);
    }

    [Fact]
    public void Coalescing_WordCharThenWordBreak_CoalescesIntoOneStep()
    {
        var bus = BusWithBuffer(out _);
        // Type "hello " — five word chars then a space
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "h"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 1), "e"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 2), "l"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 3), "l"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 4), "o"));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 5), " "));

        bus.Undo();
        Assert.False(bus.CanUndo);
    }

    [Fact]
    public void Coalescing_NonInsertEventBreaksChain_SeparateUndoSteps()
    {
        var bus = BusWithBuffer(out var buf);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "a"));
        bus.Publish(new SetCursorEvent(new CursorPosition(0, 1), new CursorPosition(0, 0)));
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 1), "b"));

        // Three separate events on stack (cursor coalesces with cursor, but insert before/after cursor are separate)
        // After undo: "b" removed, then cursor back, then "a" removed — at least 2 undos needed
        bus.Undo();
        Assert.True(bus.CanUndo);
    }

    // ── InsertTextEvent ────────────────────────────────────────────────────

    [Fact]
    public void InsertTextEvent_Execute_BufferContainsInsertedTextAndCursorAdvanced()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello");
        var ev = new InsertTextEvent(new CursorPosition(0, 5), " world");
        ev.Execute(buf);

        Assert.True(buf.InsertCalled);
        Assert.Equal(new CursorPosition(0, 11), buf.Cursor);
    }

    [Fact]
    public void InsertTextEvent_Undo_TextRemovedAndCursorReturnsToInsertionPoint()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello world");
        var ev = new InsertTextEvent(new CursorPosition(0, 5), " world");
        ev.Undo(buf);

        Assert.True(buf.DeleteCalled);
        Assert.Equal(new CursorPosition(0, 5), buf.Cursor);
    }

    [Fact]
    public void InsertTextEvent_MultiLineInsert_CursorOnCorrectLineAndColumn()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("ab");
        var ev = new InsertTextEvent(new CursorPosition(0, 2), "cd\nef");
        ev.Execute(buf);

        // "abcd\nef" → cursor should be at (line 1, col 2)
        Assert.Equal(new CursorPosition(1, 2), buf.Cursor);
    }

    // ── DeleteEvent ────────────────────────────────────────────────────────

    [Fact]
    public void DeleteEvent_Execute_RangeRemovedAndCursorAtRangeStart()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello world");
        var range = new TextRange(new CursorPosition(0, 5), new CursorPosition(0, 11));
        var ev = new DeleteEvent(range, " world");
        ev.Execute(buf);

        Assert.True(buf.DeleteCalled);
        Assert.Equal(new CursorPosition(0, 5), buf.Cursor);
    }

    [Fact]
    public void DeleteEvent_Undo_DeletedTextRestoredAndCursorAtRangeEnd()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello");
        var range = new TextRange(new CursorPosition(0, 5), new CursorPosition(0, 11));
        var ev = new DeleteEvent(range, " world");
        ev.Undo(buf);

        Assert.True(buf.InsertCalled);
        Assert.Equal(new CursorPosition(0, 11), buf.Cursor);
    }

    // ── SetCursorEvent ─────────────────────────────────────────────────────

    [Fact]
    public void SetCursorEvent_Execute_CursorAtTargetAndSelectionCleared()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello");
        var ev = new SetCursorEvent(new CursorPosition(0, 3), new CursorPosition(0, 0));
        ev.Execute(buf);

        Assert.Equal(new CursorPosition(0, 3), buf.Cursor);
        Assert.True(buf.SelectionCleared);
    }

    [Fact]
    public void SetCursorEvent_ConsecutiveMoves_CoalesceIntoSingleUndoStep()
    {
        var bus = BusWithBuffer(out var buf);
        var origin = new CursorPosition(0, 0);
        bus.Publish(new SetCursorEvent(new CursorPosition(0, 1), origin));
        bus.Publish(new SetCursorEvent(new CursorPosition(0, 2), new CursorPosition(0, 1)));
        bus.Publish(new SetCursorEvent(new CursorPosition(0, 3), new CursorPosition(0, 2)));

        bus.Undo();

        Assert.Equal(origin, buf.Cursor);
        Assert.False(bus.CanUndo);
    }

    // ── SetSelectionEvent ──────────────────────────────────────────────────

    [Fact]
    public void SetSelectionEvent_Execute_SelectionAndCursorSet()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello");
        var sel = new Selection(new CursorPosition(0, 0), new CursorPosition(0, 5));
        var ev = new SetSelectionEvent(sel, new CursorPosition(0, 5));
        ev.Execute(buf);

        Assert.Equal(sel, buf.Selection);
        Assert.Equal(new CursorPosition(0, 5), buf.Cursor);
    }

    [Fact]
    public void SetSelectionEvent_Undo_SelectionCleared()
    {
        var buf = new FakeBuffer();
        buf.Lines.Add("hello");
        var sel = new Selection(new CursorPosition(0, 0), new CursorPosition(0, 5));
        var ev = new SetSelectionEvent(sel, new CursorPosition(0, 5));
        ev.Execute(buf);
        ev.Undo(buf);

        Assert.Null(buf.Selection);
    }

    // ── SetBuffer ──────────────────────────────────────────────────────────

    [Fact]
    public void SetBuffer_ClearsBothStacks()
    {
        var bus = BusWithBuffer(out _);
        bus.Publish(new InsertTextEvent(new CursorPosition(0, 0), "x"));
        bus.Undo();
        Assert.True(bus.CanRedo);
        Assert.False(bus.CanUndo);

        var buf2 = new FakeBuffer();
        buf2.Lines.Add("fresh");
        bus.SetBuffer(buf2);

        Assert.False(bus.CanUndo);
        Assert.False(bus.CanRedo);
    }

    [Fact]
    public void Buffer_BeforeSetBuffer_ThrowsInvalidOperationException()
    {
        var bus = CreateBus();
        Assert.Throws<InvalidOperationException>(() => _ = bus.Buffer);
    }

    // ── FakeBuffer ─────────────────────────────────────────────────────────

    private sealed class FakeBuffer : IMutableTextBuffer
    {
        public List<string> Lines { get; } = [];
        public bool InsertCalled    { get; private set; }
        public bool DeleteCalled    { get; private set; }
        public bool SelectionCleared { get; private set; }

        public void Reset()
        {
            InsertCalled = false;
            DeleteCalled = false;
            SelectionCleared = false;
        }

        public int           LineCount         => Lines.Count;
        public CursorPosition Cursor           { get; private set; }
        public Selection?     Selection        { get; private set; }
        public bool           IsDirty          => false;
        public string?        FilePath         => null;
        public string?        DetectedLanguage => null;

        public string GetLine(int lineIndex) =>
            lineIndex < Lines.Count ? Lines[lineIndex] : string.Empty;

        public void InsertText(CursorPosition at, string text) { InsertCalled = true; }
        public void DeleteRange(TextRange range)               { DeleteCalled = true; }
        public void SetCursor(CursorPosition pos)              { Cursor = pos; }
        public void SetSelection(Selection? selection)
        {
            Selection = selection;
            if (selection is null) SelectionCleared = true;
        }
        public void ResizeCache(int terminalHeight) { }
    }
}
