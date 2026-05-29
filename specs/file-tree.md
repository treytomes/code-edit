# Spec: File Tree Panel

## Status
Draft

## Overview
A toggleable file tree panel on the left side of the editor, showing the directory tree rooted at the working directory. The user can navigate directories and open files from the tree. Opening a file opens it in a new tab (spec: tabs.md). The panel is hidden by default and toggled with Ctrl+B.

## Scope

**In scope**
- Left-side panel showing a directory tree rooted at the working directory (cwd at launch, or the directory of the first file opened from the command line)
- Keyboard navigation: arrow keys to move, Enter/Space to expand/collapse directories or open files
- Mouse support: single click to select, double-click to open files or toggle directories
- Toggle show/hide with Ctrl+B (keyboard) and View menu item
- Fixed panel width of 30 columns; not resizable in v2
- Auto-hide when terminal width drops below 60 columns (panel width + minimum editor width)
- Scroll within the tree when entries exceed the visible height
- Visual indicators: `▶` collapsed dir, `▼` expanded dir, space-prefixed for files; indent by 2 per depth level
- Directories sorted before files; both sorted case-insensitively within their group
- Hidden files/directories (names starting with `.`) shown — user can see `.gitignore`, `.code-edit/`, etc.

**Out of scope**
- File operations from the tree (rename, delete, new file/folder) — v3+
- Drag and drop
- Multiple roots / workspace folders
- File watching / live refresh (tree is populated on open and on manual refresh)
- Search/filter within the tree
- Context menus

## Design

### Working directory root

At launch the tree root is determined in order:
1. If a file path is given on the command line, root = that file's parent directory.
2. Otherwise, root = `Environment.CurrentDirectory`.

The root path is displayed as a header above the tree (basename only, e.g. `my-project/`).

### Component: `FileTreeView`

`FileTreeView : View` lives in `CodeEdit.Presentation.Views`. It owns:

```csharp
private string              _root;
private List<TreeEntry>     _entries;   // flattened visible entries
private int                 _selected;  // index into _entries
private int                 _scrollTop;
private HashSet<string>     _expanded;  // set of expanded dir paths
```

```csharp
private sealed record TreeEntry(string Path, string Name, bool IsDirectory, int Depth);
```

**Population**: `Populate()` walks the tree depth-first, adding entries for every item under `_root` whose parent directory is in `_expanded`. Called once at startup and exposed as a public method for manual refresh.

**Rendering** (`OnDrawingContent`): draws one entry per row from `_scrollTop`. Selected entry uses the Selection color pair. Directories are prefixed with `▶` or `▼`; files with two spaces.

**Event**: `public event EventHandler<string>? FileOpenRequested` — raised when the user confirms a file entry (Enter or double-click). AppBootstrap subscribes and opens the file in a new tab.

### Layout integration

When visible, `FileTreeView` sits to the left of `EditorView`:

```
┌─ MenuBar ──────────────────────────────────────────────────┐
│ File  Edit  Search  View  Help                             │
├─ FileTreeView ──────┬─ EditorView ───────────────────────┤
│ my-project/         │                                      │
│ ▼ src/              │                                      │
│   ▶ Domain/         │                                      │
│   Program.cs        │                                      │
│ ▶ tests/            │                                      │
│ README.md           │                                      │
├─────────────────────┴────────────────────────────────────┤
│ SearchBar (when open)                                      │
├────────────────────────────────────────────────────────────┤
│ StatusBar                                                  │
└────────────────────────────────────────────────────────────┘
```

`AppBootstrap` manages visibility and width:

```csharp
void SetTreeVisible(bool visible)
{
    fileTree.Visible      = visible;
    editorView.X          = visible ? Pos.Right(fileTree) : 0;
    editorView.Width      = visible ? Dim.Fill() - Dim.Absolute(TreeWidth) : Dim.Fill();
    viewTreeItem.Title    = (visible ? "✓ " : "  ") + "_File Tree";
}
```

**Auto-hide**: `AppBootstrap` subscribes to the window's `SizeChanged` event and calls `SetTreeVisible(false)` when `Viewport.Width < TreeWidth + MinEditorWidth` (constants: `TreeWidth = 30`, `MinEditorWidth = 30`). The user's explicit toggle preference is stored separately so restoring to a wider terminal re-shows the tree if the user had it open.

### Keyboard handling (`OnKeyDown` in `FileTreeView`)

| Key | Action |
|-----|--------|
| CursorUp / CursorDown | Move selection |
| Enter | Open file / toggle directory |
| Space | Toggle directory expand/collapse |
| Home / End | Jump to first / last entry |
| PageUp / PageDown | Scroll by viewport height |

Focus moves to `FileTreeView` when it becomes visible (Ctrl+B toggles focus as well as visibility). Pressing Ctrl+B again, or Escape, returns focus to `EditorView`.

### View menu addition

```
View
  ✓ File Tree    Ctrl+B
    Word Wrap    Alt+Z
```

## UI / UX

```
my-project/
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

Selected entry is highlighted. Scrollbar indicator (a `│` on the right edge of the panel with position marker) appears when entries exceed panel height.

## Open Questions
None.
