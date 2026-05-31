# Spec: Buffer Manager

## Status
Implemented

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

`TabEntry` is a reference type (identity semantics — two entries wrapping the same buffer are the same tab):

```csharp
public sealed class TabEntry
{
    public IMutableTextBuffer Buffer  { get; }
    public EventHistory       History { get; } = new();

    public TabEntry(IMutableTextBuffer buffer) => Buffer = buffer;
}
```

### `BufferManager`

`BufferManager` starts empty. `AppBootstrap` is responsible for adding the initial buffer via `Add()` immediately after construction.

```csharp
public sealed class BufferManager
{
    public IReadOnlyList<TabEntry> Tabs { get; }
    public int ActiveIndex { get; private set; }
    public IMutableTextBuffer ActiveBuffer => Tabs[ActiveIndex].Buffer;
    public EventHistory ActiveHistory => Tabs[ActiveIndex].History;

    // Opens a buffer in a new tab and activates it.
    // De-duplication: if a tab with the same non-null FilePath already exists,
    // activates that tab instead of opening a duplicate.
    // Multiple Untitled (FilePath == null) tabs are allowed.
    public void Add(IMutableTextBuffer buffer);

    // Activates the tab at the given index.
    public void Activate(int index);

    // Closes the tab at the given index.
    // If it was the active tab, activates the nearest remaining tab.
    // If it was the last tab, the caller must add a new buffer before calling Close,
    // or Close will throw — enforced by AppBootstrap (tabs-ui.md).
    public void Close(int index);

    public event EventHandler<int>? ActiveTabChanged;  // arg = new active index
    public event EventHandler<int>? TabClosed;         // arg = closed index
    public event EventHandler? TabsChanged;            // add or close
}
```

### `IEventBus` changes

`IEventBus` gains:

```csharp
BufferManager Buffers { get; }
event EventHandler? BufferChanged;   // fires when active tab changes
```

`IEventBus.Buffer` remains as a convenience property forwarding to `Buffers.ActiveBuffer`.

`IEventBus.SetBuffer()` is removed. This is a breaking change on the interface — all callers in `AppBootstrap` are migrated to `Buffers.Add()`, and the `EventBusTests.SetBuffer_ClearsBothStacks` test is replaced by an equivalent `BufferManagerTests` test covering the same invariant (activating a different tab starts with a fresh history).

`EventBus.Publish()` uses `Buffers.ActiveHistory.Push(ev)` (rather than the previous single `_history`).
`CanUndo`/`CanRedo`/`Undo()`/`Redo()` forward to `Buffers.ActiveHistory`.

`EventBus` subscribes to `BufferManager.ActiveTabChanged` and fires `BufferChanged`.

### `AppBootstrap` migration

`eventBus.SetBuffer(buf)` call sites become `eventBus.Buffers.Add(buf)`.
`editorView` and `searchBar` subscribe to `eventBus.BufferChanged` and refresh accordingly (same call they already make on initial buffer set).

## Open Questions
None.
