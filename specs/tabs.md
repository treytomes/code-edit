# Spec: Multiple Tabs / Buffers

## Status
Draft

## Overview
Replace the single-buffer model with a tab bar that holds multiple open buffers simultaneously. Each tab has its own undo/redo history. Tabs persist across sessions via a `.code-edit/session.json` file in the working directory. New File and Open always create a new tab rather than replacing the current buffer.

## Scope

**In scope**
- Tab bar rendered as a single row immediately below the menu bar
- Each tab shows the filename (or `Untitled` for unsaved buffers) with `*` prefix when dirty
- Scrollable tab bar when tabs exceed terminal width
- Keyboard navigation: Ctrl+Tab / Ctrl+Shift+Tab to cycle tabs; Ctrl+W to close the active tab
- Mouse: click to activate a tab
- Per-tab undo/redo history (the event bus history moves with the active buffer)
- Session persistence: open file paths saved to `.code-edit/session.json` in the working directory; restored on next launch
- New File and Open always open in a new tab

**Out of scope**
- Drag to reorder tabs
- Tab tear-off / split panes
- Pinned tabs
- Tab groups

## Design

### Buffer manager

A new `BufferManager` class (in `CodeEdit.Application`) replaces the single-buffer slot on `IEventBus`:

```csharp
public sealed class BufferManager
{
    public IReadOnlyList<TabEntry> Tabs { get; }
    public int ActiveIndex { get; }
    public IMutableTextBuffer ActiveBuffer => Tabs[ActiveIndex].Buffer;

    public void Add(IMutableTextBuffer buffer);
    public void Activate(int index);
    public void Close(int index);            // fires TabClosed event

    public event EventHandler<int>? ActiveTabChanged;
    public event EventHandler<int>? TabClosed;
    public event EventHandler? TabsChanged;
}

public sealed record TabEntry(IMutableTextBuffer Buffer, EventHistory History);
```

`EventHistory` is the existing undo/redo stack, extracted out of `EventBus` into a standalone class so each tab owns its own instance.

### IEventBus changes

`IEventBus.Buffer` becomes a forwarding property: `=> _bufferManager.ActiveBuffer`.
`IEventBus.CanUndo`, `CanRedo`, `Undo()`, `Redo()` forward to the active tab's `EventHistory`.
`SetBuffer()` is removed; callers use `BufferManager.Add()` / `BufferManager.Activate()` instead.

The event bus subscribes to `BufferManager.ActiveTabChanged` and raises its own `BufferChanged` event so `EditorView` and `SearchBarView` know to refresh.

### Component: `TabBarView`

`TabBarView : View` in `CodeEdit.Presentation.Views`:

- Height = 1 row, full width, positioned below the menu bar
- Renders tab titles left-to-right: `[ Untitled ] [ *Program.cs ] [ README.md ]`
- `_scrollOffset` tracks how many tabs are scrolled off the left edge
- Active tab is highlighted with the Selection color pair; others use Normal
- Mouse click on a tab activates it; scroll left/right on the tab bar shifts `_scrollOffset`

```csharp
public sealed class TabBarView : View
{
    public event EventHandler<int>? TabActivated;
    public event EventHandler<int>? TabCloseRequested;
    public void Refresh(IReadOnlyList<TabEntry> tabs, int activeIndex);
}
```

**Tab title rendering**: each tab is rendered as `[ {title} ]` where title is the filename basename, prefixed with `*` if dirty. When tabs overflow the viewport, a `◀` / `▶` indicator appears at the left/right edge; the active tab is always scrolled into view.

### Layout integration

With the tab bar added, the full layout from top to bottom is:

```
MenuBar          (1 row)
TabBar           (1 row)
FileTreeView  │  EditorView   (fill − 3 rows, or fill − 2 when search bar closed)
SearchBar        (0, 1, or 2 rows)
StatusBar        (1 row)
```

### Keyboard handling

Tab navigation is handled in `EditorView.OnKeyDown` (alongside existing global shortcuts):

| Key | Action |
|-----|--------|
| Ctrl+Tab | Activate next tab (wraps) |
| Ctrl+Shift+Tab | Activate previous tab (wraps) |
| Ctrl+W | Close active tab (unsaved-changes prompt if dirty) |

### Session persistence

`SessionService` in `CodeEdit.Infrastructure.Settings`:

```csharp
public sealed class SessionService(ILogger<SessionService> logger, string? workingDir = null)
{
    // Path: {workingDir}/.code-edit/session.json
    // workingDir defaults to Environment.CurrentDirectory
    public SessionData Load();
    public void Save(SessionData data);
}

public sealed record SessionData(
    IReadOnlyList<string> OpenFiles,  // absolute paths; Untitled buffers are omitted
    int ActiveIndex);                 // index into OpenFiles; 0 if out of range
```

**On launch**: `SessionService.Load()` is called after the working directory is known. If `session.json` exists and lists files that still exist on disk, they are opened as tabs in order. The previously active tab is restored. Missing files are silently skipped.

**On change**: `SessionService.Save()` is called whenever tabs change (open, close, activate). Only named (saved) files are persisted; `Untitled` buffers are omitted.

**Session file location**: `.code-edit/session.json` in the working directory. The directory is created if it doesn't exist. The user is responsible for adding `.code-edit/session.json` to their `.gitignore` if desired.

### Closing the last tab

Closing the last tab opens a fresh `Untitled` buffer rather than exiting the application. There is always at least one tab.

### New File and Open

`DoNew()` calls `bufferManager.Add(new EmptyBuffer())` and activates the new tab.
`DoOpen()` checks whether the chosen file is already open (by path); if so, activates that tab rather than opening a duplicate.

## UI / UX

### Tab bar mockup

```
 File  Edit  Search  View  Help
[ Untitled ]  [ *Program.cs ]  [ README.md ]  [ appsettings.json ]  ▶
```

Active tab highlighted; `*` prefix on dirty tabs; `▶` indicates more tabs scrolled off the right.

### Closing with unsaved changes

Same `MessageBox.Query` pattern as the existing New/Open/Quit guards:

```
┌─ Unsaved Changes ──────────────────────────────┐
│ Program.cs has unsaved changes. Close anyway?  │
│                  [ Yes ]  [ No ]               │
└────────────────────────────────────────────────┘
```

## Open Questions
None.
