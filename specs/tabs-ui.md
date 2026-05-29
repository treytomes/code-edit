# Spec: Tab Bar UI

## Status
Draft

## Overview
Add a `TabBarView` row below the menu bar that displays the open tabs, supports mouse activation, scrolls when tabs overflow the terminal width, and provides keyboard shortcuts for cycling and closing tabs. Prerequisite: `buffer-manager.md`.

## Scope

**In scope**
- `TabBarView` rendered as one row immediately below the menu bar
- Tab titles: basename of `FilePath`, or `Untitled` for unsaved buffers; `*` prefix when dirty
- Scrollable when tabs exceed terminal width; active tab always scrolled into view
- Mouse click to activate a tab
- Keyboard: Ctrl+Tab (next), Ctrl+Shift+Tab (previous), Ctrl+W (close active)
- Unsaved-changes prompt on close (consistent with existing New/Open/Quit guards)
- View > Tab Bar toggle (visible by default; can be hidden to reclaim a row)

**Out of scope**
- Drag to reorder tabs
- Tab tear-off / split panes
- Pinned tabs
- Right-click context menu on tabs

## Design

### `TabBarView`

```csharp
public sealed class TabBarView : View
{
    public event EventHandler<int>? TabActivated;
    public event EventHandler<int>? TabCloseRequested;

    // Called by AppBootstrap whenever BufferManager state changes.
    public void Refresh(IReadOnlyList<TabEntry> tabs, int activeIndex);
}
```

**Rendering**: each tab is drawn as `[ {title} ]` with one space of padding between tabs. The active tab uses the Selection color pair; others use Normal. When tabs extend beyond the viewport, `◀` and `▶` scroll indicators appear at the left and right edges respectively. `_scrollOffset` (number of tabs scrolled off the left) is adjusted so the active tab is always visible.

**Dirty indicator**: `TabBarView` subscribes to no events directly. `AppBootstrap` calls `tabBar.Refresh(...)` in response to `eventBus.BufferChanged` and `eventBus.EventExecuted` (to catch the dirty-flag change).

### Layout

```
MenuBar      Y=0,          Height=1
TabBar       Y=1,          Height=1
EditorView   Y=2,          Height=Fill−(2+searchBarHeight+1)
SearchBar    Y=AnchorEnd(1+searchBarHeight)
StatusBar    Y=AnchorEnd(1), Height=1
```

### Keyboard handling

Added to `EditorView.OnKeyDown`:

| Key | Action |
|-----|--------|
| Ctrl+Tab | `eventBus.Buffers.Activate((activeIndex + 1) % count)` |
| Ctrl+Shift+Tab | `eventBus.Buffers.Activate((activeIndex − 1 + count) % count)` |
| Ctrl+W | Fires `TabCloseRequested` for the active index |

`TabCloseRequested` is handled in `AppBootstrap`: prompts if dirty, then calls `eventBus.Buffers.Close(index)`.

### View menu addition

```
View
    File Tree    Ctrl+B        ← added in file-tree.md
  ✓ Tab Bar
    Word Wrap    Alt+Z
```

## UI / UX

### Tab bar mockup

```
 File  Edit  Search  View  Help
[ Untitled ]  [ *Program.cs ]  [ README.md ]  [ appsettings.json ]  ▶
```

Active tab highlighted. `*` prefix on dirty. `▶` when more tabs are off-screen to the right; `◀` when scrolled right.

### Close with unsaved changes

```
┌─ Unsaved Changes ─────────────────────────────────┐
│ Program.cs has unsaved changes. Close anyway?     │
│                   [ Yes ]  [ No ]                 │
└───────────────────────────────────────────────────┘
```

## Open Questions
None.
