# Spec: Event History Extraction

## Status
Implemented

## Overview
Extract the undo/redo stack out of `EventBus` into a standalone `EventHistory` class. This is a pure internal refactor with no user-visible change. It is a prerequisite for per-tab undo/redo (each tab will own its own `EventHistory` instance).

## Scope

**In scope**
- New `EventHistory` class in `CodeEdit.Application` encapsulating the undo stack, redo stack, coalescing logic, and `CanUndo`/`CanRedo`/`Undo()`/`Redo()` surface
- `EventBus` delegates to a single `EventHistory` instance (behaviour unchanged from the outside)
- All existing `EventBus` tests continue to pass without modification
- New unit tests directly targeting `EventHistory`

**Out of scope**
- Any UI change
- Multiple histories
- Connecting `EventHistory` to tabs (that is `buffer-manager.md`)

## Design

### `EventHistory`

```csharp
public sealed class EventHistory
{
    public bool CanUndo { get; }
    public bool CanRedo { get; }

    public void Push(IBufferEvent ev);          // clears redo stack, coalesces if applicable
    public IBufferEvent? TryUndo();             // returns event to un-execute, or null
    public IBufferEvent? TryRedo();             // returns event to re-execute, or null
    public void Clear();
}
```

Coalescing logic (currently in `EventBus`) moves here unchanged: consecutive `InsertTextEvent` instances within the coalesce window are merged.

### `EventBus` after refactor

`EventBus` holds `private readonly EventHistory _history = new()` and delegates:

```csharp
public bool CanUndo => _history.CanUndo;
public bool CanRedo => _history.CanRedo;
public void Undo() { var ev = _history.TryUndo(); if (ev != null) { ev.Undo(_buffer); EventUndone?.Invoke(...); } }
public void Redo() { var ev = _history.TryRedo(); if (ev != null) { ev.Execute(_buffer); EventRedone?.Invoke(...); } }
```

`EventBus.Publish()` calls `_history.Push(ev)` after executing.

**Logging**: `EventHistory` has no logger dependency and returns `null` from `TryUndo()`/`TryRedo()` when the respective stack is empty. The existing "Undo called with empty undo stack" / "Redo called with empty redo stack" warning logs stay in `EventBus`, which checks for `null` before proceeding.

### Tests

New `EventHistoryTests` covers: push/undo/redo round-trip, coalescing, redo-stack cleared on new push, `Clear()`. Existing `EventBusTests` are unchanged.

## Open Questions
None.
