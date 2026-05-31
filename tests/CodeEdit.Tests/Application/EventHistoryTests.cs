using CodeEdit.Application;
using CodeEdit.Application.Events;
using CodeEdit.Domain;

namespace CodeEdit.Tests.Application;

public sealed class EventHistoryTests
{
    private static InsertTextEvent Insert(int col, string text) =>
        new(new CursorPosition(0, col), text);

    // ── Push / CanUndo / CanRedo ───────────────────────────────────────────

    [Fact]
    public void Push_SingleEvent_CanUndoTrueCanRedoFalse()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        Assert.True(h.CanUndo);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedoStack()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        h.TryUndo();
        Assert.True(h.CanRedo);

        h.Push(Insert(0, "b"));
        Assert.False(h.CanRedo);
    }

    // ── TryUndo / TryRedo ─────────────────────────────────────────────────

    [Fact]
    public void TryUndo_WithEvent_ReturnsEventAndMovesToRedoStack()
    {
        var h = new EventHistory();
        var ev = Insert(0, "a");
        h.Push(ev);

        var result = h.TryUndo();

        Assert.Same(ev, result);
        Assert.False(h.CanUndo);
        Assert.True(h.CanRedo);
    }

    [Fact]
    public void TryUndo_EmptyStack_ReturnsNull()
    {
        var h = new EventHistory();
        Assert.Null(h.TryUndo());
    }

    [Fact]
    public void TryRedo_AfterUndo_ReturnsEventAndMovesToUndoStack()
    {
        var h = new EventHistory();
        var ev = Insert(0, "a");
        h.Push(ev);
        h.TryUndo();

        var result = h.TryRedo();

        Assert.Same(ev, result);
        Assert.True(h.CanUndo);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void TryRedo_EmptyStack_ReturnsNull()
    {
        var h = new EventHistory();
        Assert.Null(h.TryRedo());
    }

    // ── Coalescing ─────────────────────────────────────────────────────────

    [Fact]
    public void Push_AdjacentSingleCharInserts_CoalescesIntoOne()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        h.Push(Insert(1, "b"));

        h.TryUndo();
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Push_CoalescedEvent_ReturnsmergedEvent()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        var stored = h.Push(Insert(1, "b"));

        Assert.NotSame(Insert(0, "a"), stored); // it's the merged result
        Assert.False(h.CanRedo);                // redo stack cleared
    }

    [Fact]
    public void Push_NonAdjacentInserts_KeepsSeparateUndoSteps()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        h.Push(Insert(5, "b")); // gap — not adjacent

        h.TryUndo();
        Assert.True(h.CanUndo);
    }

    [Fact]
    public void Push_CoalesceDoesNotCarryOverRedoStack()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        h.TryUndo();
        Assert.True(h.CanRedo);

        // A new push that coalesces should still clear redo
        h.Push(Insert(0, "b"));
        h.Push(Insert(1, "c")); // coalesces with "b"
        Assert.False(h.CanRedo);
    }

    // ── Clear ──────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_ResetsBothStacks()
    {
        var h = new EventHistory();
        h.Push(Insert(0, "a"));
        h.TryUndo();
        Assert.True(h.CanRedo);

        h.Clear();

        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
    }
}
