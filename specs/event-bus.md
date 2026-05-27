# Spec: Event Bus

## Status
Approved

## Overview

The event bus is the central mutation coordinator for code-edit. Every change to the text buffer — whether from a keystroke, a menu action, or a search/replace operation — flows through the bus as an `IBufferEvent`. The bus applies the event to the buffer, manages the undo/redo stacks, performs word-level coalescing of consecutive insertions, and fires change notifications that drive view refresh. The bus also owns the `IBufferEvent` implementations for the v1 editing operations: insert text, delete, cursor movement, and selection.

## Scope

### In scope
- `EventBus` implementation (Application) — publish, undo, redo, coalescing
- `IBufferEvent` concrete implementations (Application/Events):
  - `InsertTextEvent` — insert a string at a position (with word-level coalescing)
  - `DeleteEvent` — delete a character or range
  - `SetCursorEvent` — move cursor to an absolute position
  - `SetSelectionEvent` — set or clear selection
- Word-level coalescing rules for `InsertTextEvent`
- Undo/redo stack behaviour
- `IEventBus` receives `IMutableTextBuffer` at construction (not per-call)
- Notification events: `EventExecuted`, `EventUndone`, `EventRedone`

### Out of scope
- Keybinding map and keystroke dispatch (editor view spec)
- Clipboard commands: cut, copy, paste (edit commands spec)
- Search/replace commands (search spec)
- Menu wiring (UI layout spec)
- Per-command `CanExecute` (menu spec)

## Design

### `EventBus` (Application)

The bus holds a reference to the active `IMutableTextBuffer`, set at construction. It owns two stacks: an undo stack and a redo stack.

```csharp
public sealed class EventBus(
    IMutableTextBuffer buffer,
    ILogger<EventBus> logger) : IEventBus
{
    private readonly IMutableTextBuffer _buffer = buffer;
    private readonly Stack<IBufferEvent> _undoStack = new();
    private readonly Stack<IBufferEvent> _redoStack = new();
}
```

#### Publish flow

```
1. incoming.Execute(_buffer)
2. If _undoStack is non-empty:
       top = _undoStack.Peek()
       if top.TryCoalesce(incoming, out merged):
           _undoStack.Pop()
           _undoStack.Push(merged)
           _redoStack.Clear()
           fire EventExecuted(merged)
           return
3. _undoStack.Push(incoming)
4. _redoStack.Clear()
5. fire EventExecuted(incoming)
```

**Logging:** log `Debug` for every published event (type name + brief description).

#### Undo flow

```
1. if _undoStack is empty: log Warning, return
2. event = _undoStack.Pop()
3. event.Undo(_buffer)
4. _redoStack.Push(event)
5. fire EventUndone(event)
```

#### Redo flow

```
1. if _redoStack is empty: log Warning, return
2. event = _redoStack.Pop()
3. event.Execute(_buffer)
4. _undoStack.Push(event)
5. fire EventRedone(event)
```

#### CanUndo / CanRedo

```csharp
public bool CanUndo => _undoStack.Count > 0;
public bool CanRedo => _redoStack.Count > 0;
```

---

### `IEventBus` update

`IEventBus` gains a `Buffer` property so views can read document state without a separate reference:

```csharp
public interface IEventBus
{
    ITextBuffer Buffer { get; }      // read-only view of the active buffer
    void Publish(IBufferEvent bufferEvent);
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    event EventHandler<BufferEventArgs> EventExecuted;
    event EventHandler<BufferEventArgs> EventUndone;
    event EventHandler<BufferEventArgs> EventRedone;
}
```

`EventBus.Buffer` returns `_buffer` (which is `IMutableTextBuffer`, assignable to `ITextBuffer`).

---

### `IBufferEvent` implementations

All live in `CodeEdit.Application/Events/`.

#### `InsertTextEvent`

```csharp
public sealed class InsertTextEvent(CursorPosition at, string text) : IBufferEvent
{
    public CursorPosition At   { get; } = at;
    public string         Text { get; private set; } = text;

    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.InsertText(At, Text);
        // Advance cursor to end of inserted text
        var newPos = EndPosition(At, Text);
        buffer.SetCursor(newPos);
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        var end = EndPosition(At, Text);
        buffer.DeleteRange(new TextRange(At, end));
        buffer.SetCursor(At);
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        if (next is not InsertTextEvent other) return false;

        // The incoming insertion must start exactly where this one ended
        var thisEnd = EndPosition(At, Text);
        if (other.At != thisEnd) return false;

        // A word-break character ends the current coalesce run but still joins
        // (so "hello " undoes as one step). Two consecutive word-break chars
        // each start a fresh run.
        if (IsWordBreak(Text[^1]) && IsWordBreak(other.Text[0])) return false;

        merged = new InsertTextEvent(At, Text + other.Text);
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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

    private static bool IsWordBreak(char c) =>
        c is ' ' or '\t' or '\n' or '\r'
            or '.' or ',' or ';' or ':' or '!' or '?'
            or '(' or ')' or '[' or ']' or '{' or '}'
            or '<' or '>' or '/' or '\\' or '-' or '_'
            or '"' or '\'';
}
```

**Coalescing rule summary:**
- Two adjacent single-character insertions coalesce when the incoming char starts at the end of the previous insertion AND they are not both word-break characters.
- Example: typing `h`, `e`, `l`, `l`, `o`, ` ` → one undo step (`hello `).
- Example: typing `h`, `e`, then pressing Left (which inserts nothing but is a non-insert event) → separate undo steps for `h` and `e`.
- Two consecutive word-break chars (e.g. `  ` — two spaces) do NOT coalesce, so each whitespace run stays bounded.

#### `DeleteEvent`

```csharp
public sealed class DeleteEvent(TextRange range, string deletedText) : IBufferEvent
{
    public TextRange Range       { get; } = range;
    public string    DeletedText { get; } = deletedText;

    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.DeleteRange(Range);
        buffer.SetCursor(Range.Start);
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.InsertText(Range.Start, DeletedText);
        buffer.SetCursor(Range.End);
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false; // Delete events are not coalesced in v1
    }
}
```

`DeletedText` is captured by the caller before publishing — the caller reads the text from the buffer, constructs `DeleteEvent`, then publishes. This keeps `DeleteEvent` self-contained for undo.

#### `SetCursorEvent`

```csharp
public sealed class SetCursorEvent(CursorPosition to, CursorPosition from) : IBufferEvent
{
    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.SetCursor(to);
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.SetCursor(from);
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        // Consecutive cursor moves collapse: the final position is what matters for undo
        if (next is not SetCursorEvent other) return false;
        merged = new SetCursorEvent(other.to, from);
        return true;
    }
}
```

**Note:** cursor movement collapses in the undo stack — pressing the arrow key ten times and then Undo jumps back to where the cursor was before the first move. This matches VS Code behaviour.

#### `SetSelectionEvent`

```csharp
public sealed class SetSelectionEvent(Selection? selection, CursorPosition cursorTo) : IBufferEvent
{
    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.SetSelection(selection);
        buffer.SetCursor(cursorTo);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }
}
```

---

### Coalescing and the undo stack invariant

The undo stack always contains the minimal set of events needed to restore the prior state. After `TryCoalesce` succeeds, the top of the stack is replaced with `merged`. Both events have already been individually executed on the buffer, so `merged.Execute` is NOT called again. The invariant: **the buffer state and the undo stack are always consistent**.

When `TryCoalesce` returns true, the merged event must represent the combined state such that calling `merged.Undo` from the post-second-execute state correctly restores the pre-first-execute state.

For `InsertTextEvent`: `merged = new InsertTextEvent(first.At, first.Text + second.Text)`. Undoing this deletes the entire combined text from `first.At`, which is correct.

---

### `AppBootstrap` DI update

`EventBus` now requires `IMutableTextBuffer` at construction. Since `LazyFileBuffer` is not constructed until a file is opened (which happens at runtime, not DI composition time), the bus must be lazily bound to a buffer.

Two options:
- **Option A:** Register `EventBus` as transient and construct it after `FileService.Open`. Simpler, but `IEventBus` loses singleton semantics (views would need to be re-wired on every file open).
- **Option B:** `EventBus` starts with a null buffer and exposes `void SetBuffer(IMutableTextBuffer)`. Called by the open-file flow after `FileService.Open`. Views that subscribe to events are already wired at startup.

**Decision: Option B.** Add `SetBuffer(IMutableTextBuffer)` to both `IEventBus` and `EventBus`. `Buffer` returns the current buffer (throws `InvalidOperationException` if called before `SetBuffer`).

Updated `IEventBus`:

```csharp
public interface IEventBus
{
    ITextBuffer Buffer { get; }
    void SetBuffer(IMutableTextBuffer buffer);
    void Publish(IBufferEvent bufferEvent);
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    event EventHandler<BufferEventArgs> EventExecuted;
    event EventHandler<BufferEventArgs> EventUndone;
    event EventHandler<BufferEventArgs> EventRedone;
}
```

When `SetBuffer` is called (e.g. after opening a new file), both stacks are cleared and `Buffer` is updated.

## UI / UX

The event bus has no UI. Views subscribe to `IEventBus.EventExecuted` / `EventUndone` / `EventRedone` and call `SetNeedsDisplay()` in their handlers. This is covered in the editor view spec.

## Test Cases

All tests in `tests/CodeEdit.Tests/Application/EventBusTests.cs`.

### Publish
1. Publish an event → `Execute` is called on the buffer, `EventExecuted` fires
2. Publish an event → it appears on the undo stack (`CanUndo == true`)
3. Publish an event → redo stack is cleared (`CanRedo == false`)

### Undo / Redo
4. Undo with an event on the stack → `Undo` called on buffer, `EventUndone` fires, `CanRedo == true`
5. Undo when stack is empty → no exception, `EventUndone` does not fire
6. Redo after undo → `Execute` called again, `EventRedone` fires
7. Redo when stack is empty → no exception, `EventRedone` does not fire
8. Publish after undo → redo stack is cleared

### Coalescing (InsertTextEvent)
9. Two adjacent single-char insertions → coalesce into one undo step
10. Insertion followed by non-adjacent insertion → separate undo steps
11. Two consecutive word-break chars → separate undo steps
12. Word char followed by word-break → coalesce (one undo step for `hello `)
13. Non-insert event between two insertions → breaks coalesce chain

### InsertTextEvent
14. Execute → buffer contains inserted text, cursor advanced to end of insertion
15. Undo → inserted text removed, cursor returns to insertion point
16. Multi-line insert (text contains `\n`) → cursor on correct line/col after execute

### DeleteEvent
17. Execute → range removed from buffer, cursor at range start
18. Undo → deleted text restored, cursor at range end

### SetCursorEvent
19. Execute → cursor at target position, selection cleared
20. Consecutive cursor moves coalesce → single undo step restores original position

### SetSelectionEvent
21. Execute → selection and cursor set
22. Undo → selection cleared

### SetBuffer
23. `SetBuffer` clears undo and redo stacks
24. `Buffer` before `SetBuffer` throws `InvalidOperationException`

## Open Questions

1. **Undo stack depth limit:** Resolved — unlimited for v1.

2. **Cursor-move coalescing scope:** Resolved — coalesce (VS Code behaviour confirmed).
