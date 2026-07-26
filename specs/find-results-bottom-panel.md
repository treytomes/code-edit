# Spec: Find Results Bottom Panel

## Status
Implemented

## Overview
Find in Files results currently live in the left sidebar under a "Search" tab alongside the file tree. This spec moves the results into a dedicated bottom panel — a closeable tile that sits between the editor area and the status bar, similar to VS Code's bottom panel. The sidebar reverts to a single-purpose file tree container with no tab strip. The bottom panel opens automatically when a Find in Files search completes and can be dismissed with `F4` or `Escape` (when the panel is focused). This change gives the file tree its full sidebar height back and gives search results significantly more horizontal space to display long match lines.

## Scope

### In scope
- Remove the two-tab strip (`SidebarTab` enum, `ShowFilesPanel`, `ShowSearchPanel`, tab rendering) from `SidebarView`
- `SidebarView` becomes a thin pass-through container for `FileTreeView` only; the `TabRowHeight` constant and all tab-drawing code are deleted
- `SidebarView.Height` expression loses the `TabRowHeight` offset and returns to `Dim.Fill() - Dim.Absolute(1)`
- `FindResultsPanel` is extracted from `SidebarView` and placed as a standalone bottom panel view in the main window
- Bottom panel has a fixed default height of 10 rows, configurable as a constant (`ResultsPanelHeight = 10`); runtime resize is out of scope
- A header row (1 row tall) drawn by `FindResultsPanel` itself shows the search summary text on the left and an `[x]` close button on the right
- Pressing `[x]` (mouse click) or `Escape` while the panel is focused fires a new `CloseRequested` event; `AppBootstrap` handles this event by hiding the panel
- `F4` toggles the panel visibility from anywhere (editor focused or panel focused); added as a keybinding in `AppBootstrap` and to `EditorView`'s key handler
- When the panel is hidden, `editorView.Height` expands to fill the recovered rows
- When the panel is shown, `editorView.Height` and the inline `searchBar` position adjust to account for the panel height
- After a Find in Files search completes, the panel is shown automatically (`ShowResultsPanel()` in `AppBootstrap`); `SetSidebarVisible(true)` / `sidebar.ShowSearchPanel()` calls in `DoFindInFiles` are removed
- `SearchContext`, `SearchContextKind`, `SetFindInFilesContext`, `MoveNext`, `MovePrev`, `HighlightResult`, and the F3/Shift+F3 result-navigation flow are unchanged
- View menu gains a `"  _Find Results"` toggle item (checkmark reflects panel visibility); `F4` shown as its shortcut hint
- Keyboard shortcuts help text updated to replace the `Ctrl+B  Toggle file tree / search panel` line with separate entries for file tree (`Ctrl+B`) and find results panel (`F4`)
- `DoFindInFiles` in `AppBootstrap` stops calling `sidebar.ShowSearchPanel()` and `SetSidebarVisible(true)`; it calls the new `ShowResultsPanel()` helper instead

### Out of scope
- Runtime resize of the bottom panel (drag handle, configurable height via settings)
- A second use for the bottom panel (e.g., build output, terminal)
- Persisting panel open/closed state across sessions
- Removing the `FindResultsPanel` singleton registration from the DI container (it stays registered)
- Any changes to `SearchContext`, `FindInFilesService`, `FindInFilesDialog`, or `SearchBarView`

## Design

### Constants (AppBootstrap)

```csharp
private const int TreeWidth              = 30;   // unchanged
private const int MinEditorWidth         = 30;   // unchanged
private const int DefaultResultsPanelHeight = 10;
```

`ResultsPanelHeight` is no longer a constant — it is loaded from settings at startup and saved back whenever it would change (currently only at startup from settings; runtime resize is out of scope, but the value is persisted so a future resize feature can write it).

### Layout state

`AppBootstrap` gains two new locals:

```csharp
var resultsPanelVisible = false;
var resultsPanelHeight  = settings.ResultsPanelHeight;  // loaded from settings
```

`currentBarH` (tracks `searchBar` height) is unchanged; the panel is independent of the inline search bar.

### `UpdateEditorLayout` geometry

The updated helper must account for three variable-height regions below the `tabBar`: the bottom panel, the inline search bar, and the status bar.

```
panelRows  = resultsPanelVisible ? resultsPanelHeight : 0
bottomRows = 1 + panelRows      // status bar (1) + panel

editorView.Height = Dim.Fill() - Dim.Absolute(bottomRows + currentBarH)
searchBar.Y       = Pos.AnchorEnd(1 + panelRows + currentBarH)
searchBar.Height  = Dim.Absolute(currentBarH)

resultsPanel.Y      = Pos.AnchorEnd(1 + panelRows)
resultsPanel.Height = Dim.Absolute(panelRows)
resultsPanel.Visible = resultsPanelVisible
```

`sidebar.Height` is always `Dim.Fill() - Dim.Absolute(1)` (no longer offset for panel height — the sidebar and panel share the window vertically through the editor column, not the sidebar column).

### `FindResultsPanel` changes

`FindResultsPanel` gains:

```csharp
public event EventHandler? CloseRequested;
```

A new `_headerRows` constant (value `1`) records that the first rendered row is always the header. The existing `_rows` list starts at visual row 1; `OnDrawingContent` renders row 0 as the header.

**Header row rendering** (inside `OnDrawingContent`, row 0):

- Left side: the current summary string (same text as the first `Notice` row that was previously the first entry in `_rows`)
- Right side: `[x]` right-aligned at `Viewport.Width - 3`
- Color: uses the `theme.StatusBar` color pair (distinct from the result rows)

The `Notice` header row that `SetResults` previously prepended to `_rows` is removed from `_rows`; the summary text is stored separately in a `_summary` field and rendered directly in the header:

```csharp
private string _summary = "";

public void SetResults(string queryDisplay, IReadOnlyList<FileMatches> results)
{
    _summary = queryDisplay;
    // _rows populated as before, minus the leading Notice row
    ...
}

public void SetSearching()
{
    _summary = "Searching…";
    ...
}

public void Clear()
{
    _summary = "";
    ...
}
```

**Row index shift**: because the header now consumes visual row 0, all existing `AddStr(0, row, ...)` calls in `OnDrawingContent` shift to `row + 1`. `Viewport.Height` available for result rows is `Viewport.Height - 1`. `EnsureVisible` uses `Viewport.Height - 1` as `h`.

**Close button mouse hit**: in `OnMouseEvent`, if the click lands on row 0 and column `>= Viewport.Width - 3`, fire `CloseRequested`.

**Escape key**: in `OnKeyDown`, if `key.KeyCode == KeyCode.Esc`, fire `CloseRequested`, set `key.Handled = true`, and return `true`.

**`[x]` click area rendering** (inside the header row):

```
 query text (15 matches in 3 files)                     [x]
```

The `[x]` occupies columns `Viewport.Width - 3` through `Viewport.Width - 1`.

### `EditorSettings` and `SettingsService` changes

`EditorSettings` gains one new field:

```csharp
public sealed record EditorSettings(
    int     TabWidth,
    bool    InsertSpaces,
    int     RecentFilesMax,
    string? ActiveTheme          = null,
    int     ResultsPanelHeight   = 10)
{
    public static readonly EditorSettings Default = new(
        TabWidth: 4, InsertSpaces: true, RecentFilesMax: 10);
    ...
}
```

`SettingsService.Load()` reads `resultsPanelHeight` from JSON (same validation: must be `> 0`, fallback to `DefaultResultsPanelHeight` if absent or invalid).

`SettingsService` gains a new `SaveResultsPanelHeight(int height)` method following the same patch-write pattern as `SaveActiveTheme` — reads the existing JSON object, upserts the `"resultsPanelHeight"` key, and writes back.

`AppBootstrap` calls `settingsSvc.SaveResultsPanelHeight(resultsPanelHeight)` nowhere in this spec (runtime resize is out of scope), but the field is loaded on startup so the user can set a custom default by editing `settings.json` manually.

### `SidebarView` changes

`SidebarView` is stripped to a minimal container:

```csharp
public sealed class SidebarView : View
{
    private readonly FileTreeView _fileTree;

    public SidebarView(FileTreeView fileTree)
    {
        _fileTree        = fileTree;
        CanFocus         = true;
        _fileTree.X      = 0;
        _fileTree.Y      = 0;
        _fileTree.Width  = Dim.Fill();
        _fileTree.Height = Dim.Fill();
        Add(_fileTree);
    }

    public void SetFocusToActivePanel() => _fileTree.SetFocus();
}
```

Removed entirely:
- `SidebarTab` enum
- `ActiveTab` property
- `FilesPanelSelected` / `SearchPanelSelected` events
- `ShowFilesPanel()` / `ShowSearchPanel()` methods
- `TabRowHeight` constant
- `OnDrawingContent` override (tab strip drawing)
- `OnKeyDown` override (left/right tab switching)
- `FindResultsPanel` constructor parameter and field

`SidebarView`'s DI constructor signature changes from `(FileTreeView, FindResultsPanel, ThemeRegistry)` to `(FileTreeView)`. The `FindResultsPanel` and `ThemeRegistry` injections are removed.

### `AppBootstrap` wiring changes

**DI**: no change — `FindResultsPanel` remains a singleton. `SidebarView` constructor now resolves with only `FileTreeView`.

**New helpers**:

```csharp
void ShowResultsPanel()
{
    resultsPanelVisible = true;
    UpdateEditorLayout();
    resultsPanel.SetFocus();
}

void HideResultsPanel()
{
    resultsPanelVisible = false;
    UpdateEditorLayout();
    editorView.SetFocus();
}

void ToggleResultsPanel()
{
    if (resultsPanelVisible) HideResultsPanel();
    else                     ShowResultsPanel();
}
```

**`DoFindInFiles` changes** — replace:
```csharp
resultsPanel.SetSearching();
sidebar.ShowSearchPanel();
SetSidebarVisible(true);
sidebarUserVisible = true;
```
with:
```csharp
resultsPanel.SetSearching();
ShowResultsPanel();
```

After the async search completes and `SetResults` is called, `ShowResultsPanel()` is already active; no second call needed.

**New event wiring**:

```csharp
resultsPanel.CloseRequested += (_, _) => HideResultsPanel();

editorView.FindResultsPanelToggleRequested += (_, _) => ToggleResultsPanel();
```

**`EditorView` new event**: `FindResultsPanelToggleRequested` is fired on `F4` in `EditorView.OnKeyDown` (same pattern as `FindInFilesRequested` on `Ctrl+Alt+F`).

**View menu item**:

```csharp
var findResultsItem = new MenuItem("  _Find Results", "F4", ToggleResultsPanel);
// keep checkmark in sync:
// called from ShowResultsPanel / HideResultsPanel:
findResultsItem.Title = (resultsPanelVisible ? "✓ " : "  ") + "_Find Results";
```

**`Escape` in sidebar** (existing handler) is unchanged — pressing Escape while the sidebar has focus still returns focus to the editor.

**Window layout block** (`// ── Layout ──`):

```csharp
resultsPanel.X       = Pos.Absolute(0);
resultsPanel.Y       = Pos.AnchorEnd(1 + ResultsPanelHeight);  // initial; UpdateEditorLayout owns this
resultsPanel.Width   = Dim.Fill();
resultsPanel.Height  = Dim.Absolute(ResultsPanelHeight);
resultsPanel.Visible = false;
```

`resultsPanel` is added to `window.Add(...)` alongside the other views.

**`sidebar.Height`**: changes from:
```csharp
sidebar.Height = Dim.Fill() - Dim.Absolute(1 + currentBarH);
```
to:
```csharp
sidebar.Height = Dim.Fill() - Dim.Absolute(1);
```
(the sidebar is not affected by panel or search bar height — it spans the full column beside the editor).

**`UpdateEditorLayout` rewrite**:

```csharp
void UpdateEditorLayout()
{
    var sideW      = sidebar.Visible ? TreeWidth : 0;
    var panelRows  = resultsPanelVisible ? ResultsPanelHeight : 0;
    var bottomRows = 1 + panelRows;

    sidebar.Height   = Dim.Fill() - Dim.Absolute(1);

    editorView.X     = Pos.Absolute(sideW);
    editorView.Width = Dim.Fill();
    editorView.Height = Dim.Fill() - Dim.Absolute(bottomRows + currentBarH);

    searchBar.Y      = Pos.AnchorEnd(1 + panelRows + currentBarH);
    searchBar.Height = Dim.Absolute(currentBarH);

    resultsPanel.Y      = Pos.AnchorEnd(1 + panelRows);
    resultsPanel.Height = Dim.Absolute(panelRows);
    resultsPanel.Visible = resultsPanelVisible;

    findResultsItem.Title = (resultsPanelVisible ? "✓ " : "  ") + "_Find Results";
}
```

Note: `resultsPanel.Y = Pos.AnchorEnd(1 + panelRows)` positions the panel so its top edge is `panelRows` rows above the status bar. When `panelRows = 0`, `Visible = false` so the zero-height expression is irrelevant.

### `EditorView` key handling

`EditorView.OnKeyDown` adds:

```csharp
if (key.KeyCode == KeyCode.F4)
{
    FindResultsPanelToggleRequested?.Invoke(this, EventArgs.Empty);
    key.Handled = true;
    return true;
}
```

New event declaration:

```csharp
public event EventHandler? FindResultsPanelToggleRequested;
```

## UI / UX

### Full layout — panel visible

```
┌── code-edit ─────────────────────────────────────────────────┐
│ File  Edit  Search  View  Help                                │  ← MenuBar
├───────────────────────────────────────────────────────────────┤
│ [ main.cs × ]  [ Program.cs × ]                              │  ← TabBarView
├──────────────┬────────────────────────────────────────────────┤
│ src/         │   1 using System;                             │
│  ▶ Domain/   │   2                                           │
│  ▼ Applic…   │   3 namespace CodeEdit.Application;           │  ← EditorView (shrunk)
│    …         │   4 ▌                                         │
│              │                                               │
│              ├────────────────────────────────────────────────┤
│              │ Find: [        ] [Aa] [W] [.*]  ◀ ▶  3/14    │  ← SearchBarView (when open)
├──────────────┴────────────────────────────────────────────────┤
│ "hello"  (14 matches in 3 files)                        [x]  │  ← FindResultsPanel header row
│ Program.cs  (4 matches)                                       │
│   12:     string hello = "hello";                             │  ← result rows (9 rows)
│   47:     hello = Greet();                                    │
│   51:     return hello;                                       │
│   89: // says hello                                           │
│ Readme.md  (10 matches)                                       │
│    3: # hello world                                           │
│    9: Say hello to the user                                   │
│   18: hello, world                                            │
├───────────────────────────────────────────────────────────────┤
│ src/Program.cs                              Ln 4, Col 1       │  ← StatusBarView
└───────────────────────────────────────────────────────────────┘
```

### Full layout — panel hidden

```
┌── code-edit ─────────────────────────────────────────────────┐
│ File  Edit  Search  View  Help                                │
├───────────────────────────────────────────────────────────────┤
│ [ main.cs × ]  [ Program.cs × ]                              │
├──────────────┬────────────────────────────────────────────────┤
│ src/         │   1 using System;                             │
│  ▶ Domain/   │   2                                           │
│  ▼ Applic…   │   3 namespace CodeEdit.Application;           │
│    …         │   4 ▌                                         │
│              │                                               │
│              │                                               │
│              │                                               │
│              │                                               │
│              │                                               │
│              │                                               │
│              │                                               │
├──────────────┴────────────────────────────────────────────────┤
│ src/Program.cs                              Ln 4, Col 1       │
└───────────────────────────────────────────────────────────────┘
```

The sidebar spans the full height (minus menu, tab bar, and status bar) in both states.

### Panel — empty/searching state

When the panel is visible but no search has completed yet:

```
│ Searching…                                              [x]  │
│                                                              │
│                                                              │
│  Press Ctrl+Alt+F                                            │
│  to search across files.                                     │
│                                                              │
│                                                              │
│                                                              │
│                                                              │
│                                                              │
```

(The header row always renders; the empty-state message appears in the content rows below it.)

### Keybindings

| Key | Action |
|-----|--------|
| `F4` | Toggle Find Results panel open/closed (from editor or panel) |
| `Escape` (panel focused) | Close panel, return focus to editor |
| `[x]` click | Close panel, return focus to editor |
| `Ctrl+Alt+F` | Open Find in Files dialog (unchanged) |
| `Ctrl+B` | Toggle file tree sidebar only (unchanged) |
| `F3` / `Shift+F3` | Next / previous result (unchanged; cross-file when context is FindInFiles) |
| `Enter` (on result row) | Open file at matched line (unchanged) |
| `Up` / `Down` (panel focused) | Navigate result rows (unchanged) |

### View menu (after this change)

```
_View
  ✓ _File Tree          Ctrl+B
    _Find Results        F4
  ✓ _Tab Bar
    _Word Wrap           Alt+Z
  ─────────────────────────
  Edit _Theme…
```

(`✓` reflects current visibility.)

## Test Cases

### Layout geometry

1. When the panel is hidden (`resultsPanelVisible = false`), `editorView.Height` uses `Dim.Fill() - Dim.Absolute(1 + currentBarH)` — same expression as before this feature.
2. When the panel is visible, `editorView.Height` uses `Dim.Fill() - Dim.Absolute(1 + ResultsPanelHeight + currentBarH)` and `searchBar.Y` shifts up by `ResultsPanelHeight` rows.
3. Toggling the panel twice (show then hide) leaves all layout expressions at their pre-show values.
4. `sidebar.Height` is `Dim.Fill() - Dim.Absolute(1)` regardless of panel state.

### `FindResultsPanel` — header and close

5. `OnDrawingContent` renders the summary string in row 0 and `[x]` at the right edge of row 0.
6. After `SetResults(summary, results)` the summary is rendered in the header row; no `Notice` row appears at the top of the scrollable result rows.
7. After `SetSearching()`, the header row shows `"Searching…"`.
8. After `Clear()`, the header row is blank and the empty-state message appears in the content rows.
9. A mouse click on row 0, columns `Viewport.Width - 3` through `Viewport.Width - 1` fires `CloseRequested`.
10. A mouse click on row 0 outside the `[x]` region does not fire `CloseRequested`.
11. Pressing `Escape` while the panel is focused fires `CloseRequested` and marks the key handled.
12. Pressing `Escape` while the editor is focused does not affect the panel (the key is consumed by `EditorView` or bubbles to the window without reaching `FindResultsPanel`).

### `SidebarView` — tab strip removal

13. `SidebarView` no longer contains `FindResultsPanel` as a child view; `FindResultsPanel` is a direct child of the main window.
14. `SidebarView` no longer draws tab labels or an underline row in `OnDrawingContent`.
15. `FileTreeView` is positioned at `Y = 0` inside `SidebarView` (no `TabRowHeight` offset).

### `AppBootstrap` wiring

16. After `DoFindInFiles` completes successfully, `resultsPanelVisible` is `true`, the panel is visible, and `FindResultsPanel` has keyboard focus.
17. `ShowResultsPanel()` calls `resultsPanel.SetFocus()` after `UpdateEditorLayout()`.
18. `DoFindInFiles` no longer calls `sidebar.ShowSearchPanel()` or forces `SetSidebarVisible(true)`.
19. `F4` in the editor fires `FindResultsPanelToggleRequested`; `AppBootstrap` calls `ToggleResultsPanel`.
20. `ToggleResultsPanel` shows the panel (and focuses it) when hidden, and hides it (and focuses the editor) when visible.
21. `CloseRequested` from the panel triggers `HideResultsPanel`; `editorView.SetFocus()` is called.
22. Canceling the Find in Files dialog leaves the panel state unchanged.
23. Auto-hide of the sidebar on narrow terminal (`< 60 cols`) is unaffected by panel state.
24. `Ctrl+B` toggles only the sidebar; the panel is unaffected.

### Settings persistence

24. `SettingsService.Load()` with `"resultsPanelHeight": 15` in JSON returns an `EditorSettings` with `ResultsPanelHeight = 15`.
25. `SettingsService.Load()` with no `resultsPanelHeight` key returns `ResultsPanelHeight = 10` (the default).
26. `SettingsService.Load()` with `"resultsPanelHeight": 0` or a negative value falls back to the default `10`.
27. `AppBootstrap` initialises `resultsPanelHeight` from `settings.ResultsPanelHeight` at startup; a value of `15` loaded from settings makes the panel 15 rows tall when shown.

### Regression

28. F3 / Shift+F3 cross-file navigation continues to work and calls `resultsPanel.HighlightResult` (unchanged).
29. Pressing Enter on a result row fires `ResultOpenRequested` and opens the file (unchanged).
30. The inline search bar (`SearchBarView`) still slides up correctly and does not overlap the panel or status bar when both are visible.

## Open Questions

None.
