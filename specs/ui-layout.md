# Spec: UI Layout

## Status
Implemented

## Overview

`AppBootstrap` wires the main window together. The layout has five regions: a `MenuBar` at the top, a `TabBarView` below it, an optional `FileTreeView` on the left, an `EditorView` filling the remaining space, a `SearchBarView` that slides up above the status bar when active, and a `StatusBarView` pinned to the bottom row.

## Scope

### In scope
- Main window layout: `MenuBar`, `TabBarView`, `FileTreeView`, `EditorView`, `SearchBarView`, `StatusBarView`
- Command-line argument: optional file path (`args[1]`)
- Startup buffer: open file or restore session; fall back to `EmptyBuffer`
- `EmptyBuffer` — minimal `IMutableTextBuffer`, single empty line, `FilePath = null`
- `StatusBarView` — file path (or `[No File]`), cursor position (`Ln X, Col Y`), dirty indicator (`*`)
- File tree auto-hide when terminal is narrower than `TreeWidth + MinEditorWidth` (30 + 30 = 60 cols)
- Full menu bar: File, Edit, Search, View, Help

### Out of scope
- Theme editor dialog (see `theme-editor.md`)
- LSP, command palette (v3+)

## Design

### Layout

```
┌── code-edit ─────────────────────────────────────────────┐
│ File  Edit  Search  View  Help                            │  ← MenuBar
├──────────────────────────────────────────────────────────┤
│ [ main.rs × ]  [ README.md × ]                           │  ← TabBarView (1 row)
├──────────────┬───────────────────────────────────────────┤
│ src/         │   1 fn main() {                           │
│  ▶ CodeEdit  │   2     println!("hello");                │
│  ▼ tests/    │   3 }                                     │  ← EditorView (fills)
│    ...       │   4 ▌                                     │
│              │                                           │
│              ├───────────────────────────────────────────┤
│              │ Find: [        ] ◀ ▶  [✓] Case  [✓] Word │  ← SearchBarView (0 or 1 row)
├──────────────┴───────────────────────────────────────────┤
│ src/main.rs                           Ln 4, Col 1        │  ← StatusBarView (1 row)
└──────────────────────────────────────────────────────────┘
```

`FileTreeView` width = 30 columns (`TreeWidth`). When hidden, `EditorView.X = 0`.

### Layout constants

```csharp
private const int TreeWidth      = 30;
private const int MinEditorWidth = 30;
```

### View positions

```csharp
tabBar.X      = 0;             tabBar.Y      = Pos.Bottom(menuBar);
tabBar.Width  = Dim.Fill();    tabBar.Height = Dim.Absolute(1);

fileTree.X      = 0;           fileTree.Y      = Pos.Bottom(tabBar);
fileTree.Width  = Dim.Absolute(TreeWidth);
fileTree.Height = Dim.Fill() - Dim.Absolute(/* tabH + statusH */);

editorView.X      = Pos.Absolute(TreeWidth);   // 0 when tree hidden
editorView.Y      = Pos.Bottom(tabBar);
editorView.Width  = Dim.Fill();   // fills from X to right edge
editorView.Height = Dim.Fill() - Dim.Absolute(/* tabH + statusH */);

searchBar.X = 0;  searchBar.Y = Pos.AnchorEnd(1 + statusH);
searchBar.Width = Dim.Fill();  searchBar.Height = Dim.Absolute(0 or 1);

statusBar.X = 0;  statusBar.Y = Pos.AnchorEnd(1);
statusBar.Width = Dim.Fill();  statusBar.Height = Dim.Absolute(1);
```

`Dim.Fill()` fills from the view's `X` position to the right edge — never subtract `TreeWidth` from `Dim.Fill()` when `X` already accounts for it.

### `EmptyBuffer`

Lives in `CodeEdit.Application`. Single empty line, writable, `FilePath = null`, `IsDirty = false`.

### Startup sequence

1. Build DI container; call `app.Init()`.
2. Parse `Environment.GetCommandLineArgs()[1]` (optional file path).
3. If a path was given: `fileService.Open(path)` → add to `BufferManager`; catch `FileServiceException`, fall back to `EmptyBuffer` and show error dialog after UI starts.
4. Otherwise: add `EmptyBuffer` as placeholder; restore session via `SessionService.Load()` — add each restorable file, close placeholder if any restored, clamp `ActiveIndex`.
5. Populate `FileTreeView` with `rootDir`.
6. Run `app.Run(window)`.

### Auto-hide file tree

`window.FrameChanged` fires on terminal resize. If `window.Viewport.Width < TreeWidth + MinEditorWidth`, the tree is hidden regardless of the user toggle. When the terminal widens again and the user's preference was visible, it reappears.

### Error handling

- Command-line open failure: logged as warning; `EmptyBuffer` used; error dialog shown via `app.Invoke` after the window starts.
- Top-level unhandled exception: logged as critical; `MessageBox` shown before clean exit (no stack trace dumped to terminal).

### `StatusBarView`

Renders one line using the `StatusBar` color pair:

```
 {filePath or "[No File]"}  {dirty? "* " : "  "}  Ln {line+1}, Col {col+1}
```

Refreshes on `EventExecuted`, `EventUndone`, `EventRedone`, and `ThemeChanged`.

## Open Questions

None.
