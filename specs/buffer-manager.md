# Spec: Buffer Manager

## Status
Draft

## Overview
Introduce `BufferManager` to hold a collection of open buffers, each with its own `EventHistory`. Update `IEventBus` to forward to the active buffer and its history. This is the infrastructure layer for multiple tabs; it has no UI of its own. Prerequisite: `event-history.md`.

## Scope

**In scope**
- `BufferManager` class in `CodeEdit.Application`
- `IEventBus.Buffer` forwarded through `BufferManager.ActiveBuffer`
- `IEventBus` undo/redo forwarded to the active tab's `EventHistory`
- `IEventBus.SetBuffer()` replaced by `BufferManager.Add()` / `BufferManager.Activate()`
- `BufferChanged` event on `IEventBus` so `EditorView` and `SearchBarView` can refresh when the active tab changes
- All existing behaviour (single buffer, undo/redo, search) continues to work correctly
- Unit tests for `BufferManager`

**Out of scope**
- Any UI (tab bar, file tree) — that is `tabs-ui.md` and `file-tree.md`
- Session persistence — that is `session.md`

## Design

### `TabEntry`

```csharp
public sealed record TabEntry(IMutableTextBuffer Buffer, EventHistory History);
```

### `BufferManager`

```csharp
public sealed class BufferManager
{
    public IReadOnlyList<TabEntry> Tabs { get; }
    public int ActiveIndex { get; private set; }
    public IMutableTextBuffer ActiveBuffer => Tabs[ActiveIndex].Buffer;
    public EventHistory ActiveHistory => Tabs[ActiveIndex].History;

    // Opens a buffer in a new tab and activates it.
    // If a tab with the same FilePath already exists, activates it instead.
    public void Add(IMutableTextBuffer buffer);

    // Activates the tab at the given index.
    public void Activate(int index);

    // Closes the tab at the given index.
    // If it was the active tab, activates the nearest remaining tab.
    // If it was the last tab, adds a fresh EmptyBuffer first.
    public void Close(int index);

    public event EventHandler<int>? ActiveTabChanged;  // arg = new active index
    public event EventHandler<int>? TabClosed;         // arg = closed index
    public event EventHandler? TabsChanged;            // add or close
}
```

`BufferManager` starts with one tab containing an `EmptyBuffer`.

### `IEventBus` changes

`IEventBus` gains:

```csharp
BufferManager Buffers { get; }
event EventHandler? BufferChanged;   // fires when active tab changes
```

`IEventBus.Buffer` remains as a convenience property forwarding to `Buffers.ActiveBuffer`.
`IEventBus.SetBuffer()` is removed; all callers use `Buffers.Add()` / `Buffers.Activate()`.

`EventBus.Publish()` uses `Buffers.ActiveHistory.Push(ev)` (rather than the previous single `_history`).
`CanUndo`/`CanRedo`/`Undo()`/`Redo()` forward to `Buffers.ActiveHistory`.

`EventBus` subscribes to `BufferManager.ActiveTabChanged` and fires `BufferChanged`.

### `AppBootstrap` migration

All `eventBus.SetBuffer(buf)` calls become `eventBus.Buffers.Add(buf)`.
`editorView` and `searchBar` subscribe to `eventBus.BufferChanged` and refresh accordingly (same call they already make on initial `SetBuffer`).

## Open Questions
None.
