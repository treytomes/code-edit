# Spec: File Tree Panel

## Status
Draft

## Overview
A toggleable left-side panel showing the directory tree of the working folder. The user can navigate and open files from the tree; opening a file adds it as a new tab. File > Open is split into "Open File" and "Open Folder", and the recent items list tracks both files and folders. Prerequisite: `tabs-ui.md`.

## Scope

**In scope**
- `FileTreeView` panel, 30 columns wide, left of `EditorView`
- Toggle with Ctrl+B; View menu item with checkmark
- Auto-hide when terminal width < 60 columns; restore when wide enough again
- Keyboard navigation within the tree: arrows, Enter, Space, Home, End, PageUp/PageDown
- Mouse: single click to select, double-click to open file / toggle directory
- `FileOpenRequested` event wired to open the file in a new tab
- File menu: "Open File…" (Ctrl+O) and "Open Folder…" — replaces current "Open…"
- Recent items list updated to include recently opened folders alongside files, with a visual distinction between the two
- Directories sorted before files; both case-insensitive within their group
- Hidden entries (names starting with `.`) are shown
- Manual refresh (F5 while tree has focus) re-reads the directory

**Out of scope**
- File operations from the tree (create, rename, delete) — v3+
- Drag and drop
- Multiple roots / workspace folders
- File watching / live refresh (tree populated on open and on F5)
- Search / filter within the tree
- Resizable panel width

## Design

### Working directory root

Determined at launch in order:
1. If a folder was opened via Open Folder, that folder.
2. If a file was given on the command line or opened via Open File, that file's parent directory.
3. Otherwise `Environment.CurrentDirectory`.

The root is stored on `AppBootstrap` state. Switching root (Open Folder) repopulates the tree and saves the new root to `session.json` (session.md).

### `FileTreeView`

```csharp
public sealed class FileTreeView : View
{
    public void Populate(string root);   // replaces current tree
    public void Refresh();               // re-reads from same root

    public event EventHandler<string>? FileOpenRequested;  // arg = absolute file path
}
```

Internal state:

```csharp
private string           _root    = "";
private List<TreeEntry>  _entries = [];   // flattened visible rows
private int              _selected;
private int              _scrollTop;
private HashSet<string>  _expanded = [];  // absolute paths of expanded directories
```

```csharp
private sealed record TreeEntry(string Path, string Name, bool IsDirectory, int Depth);
```

**Population**: depth-first walk, emitting entries for items whose parent is in `_expanded`. Directories sorted before files; both sorted case-insensitively.

**Rendering** (`OnDrawingContent`): one entry per row from `_scrollTop`. Prefix: `▼ ` expanded dir, `▶ ` collapsed dir, `  ` file. Indent 2 spaces per depth level. Selected row uses Selection color pair.

### Keyboard handling (`OnKeyDown` in `FileTreeView`)

| Key | Action |
|-----|--------|
| CursorUp / CursorDown | Move selection; scroll if needed |
| Enter | Open file or toggle directory |
| Space | Toggle directory expand/collapse |
| Home / End | First / last entry |
| PageUp / PageDown | Scroll by viewport height |
| F5 | Refresh tree |
| Escape | Return focus to EditorView |

Ctrl+B (handled in `EditorView.OnKeyDown`) toggles visibility and moves focus.

### Auto-hide

`AppBootstrap` subscribes to `window.SizeChanged`. Constants: `TreeWidth = 30`, `MinEditorWidth = 30`.

```csharp
void OnSizeChanged()
{
    var shouldShow = _treeUserVisible && window.Viewport.Width >= TreeWidth + MinEditorWidth;
    if (shouldShow != fileTree.Visible)
        SetTreeVisible(shouldShow);
}
```

`_treeUserVisible` tracks the user's explicit toggle preference so the tree re-appears when the terminal is widened again.

### File menu changes

The existing "Open…" item is replaced:

```
File
  New             Ctrl+N
  Open File…      Ctrl+O
  Open Folder…
  Open Recent  ▶
  ─────────────
  Save            Ctrl+S
  Save As…
  ─────────────
  Exit
```

### Open Folder

`DoOpenFolder()` in AppBootstrap:
1. Shows an `OpenDialog` with `OpenMode = Directory`.
2. Calls `fileTree.Populate(chosenPath)`.
3. Updates the session root.
4. Adds the folder path to the recent items list.

### Recent items list

`RecentFilesService` is extended to store a `kind` field per entry (`"file"` or `"folder"`). The recent items submenu renders them with a visual prefix:

```
Open Recent
  📄 ~/projects/code-edit/src/Program.cs
  📁 ~/projects/code-edit
  📄 ~/notes/todo.md
  ─────────────────────────────────────────
  Clear Recent Items
```

Since Terminal.Gui renders in a 16-color terminal, emoji may not display. The prefix falls back to `[F] ` for folders and `[f] ` for files if needed. (Implementation should detect terminal capability or simply use text prefixes for safety.)

`RecentFilesService.Add(string path, RecentKind kind)` — `kind` is `File` or `Folder`.
Selecting a recent folder calls `DoOpenFolder(path)` directly (no dialog).

### Layout integration

```
MenuBar
TabBar
FileTreeView (30 cols) │ EditorView (fill − 30)
SearchBar
StatusBar
```

When the file tree is hidden, `EditorView.X = 0` and `EditorView.Width = Dim.Fill()`.

## UI / UX

```
my-project/                    [F5 refresh]
▼ src/
  ▼ CodeEdit.Domain/
      CursorPosition.cs
      Selection.cs
  ▶ CodeEdit.Application/
  ▶ CodeEdit.Infrastructure/
  ▶ CodeEdit.Presentation/
▶ tests/
▶ specs/
  README.md
  CLAUDE.md
```

## Open Questions
None.
