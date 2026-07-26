# Spec: Folder Picker Dialog

## Status
Draft

## Overview
`FolderPickerDialog` is a hand-rolled `Dialog` subclass that replaces the Terminal.Gui `OpenDialog` used in `DoOpenFolder`. The built-in `OpenDialog` crashes on launch when the starting directory contains broken symlinks (a Terminal.Gui bug in `FileSystemTreeBuilder.IsReparsePoint`), its Cancel button does not work, and its color scheme is inconsistent with the rest of the application. The custom dialog is entirely our code, eliminates all three problems, and is styled through `IColorTheme` exactly like the rest of the editor.

## Scope

### In scope
- `FolderPickerDialog : Dialog` — new class in `CodeEdit.Presentation.Views`
- An inner `DirectoryListView : View` — custom scrollable list with `OnDrawingContent`, keyboard navigation, and mouse support
- Replacing the `OpenDialog` block in `AppBootstrap.DoOpenFolder` with `FolderPickerDialog`
- Removing the try/catch crash workaround around `app.Run(dlg)` in `DoOpenFolder`

### Out of scope
- Replacing `OpenDialog` for `DoOpenFile` — file-open still uses the Terminal.Gui dialog
- Drive / volume switching on Windows
- Bookmarks or favourites
- A keyboard shortcut to jump to the home directory
- Hidden-file toggle

## Design

### Public API

```csharp
public sealed class FolderPickerDialog : Dialog
{
    // True when the user pressed Cancel or Escape; false on Select Folder
    public bool   Canceled     { get; private set; } = true;

    // The directory path accepted by the user; empty string if Canceled
    public string SelectedPath { get; private set; } = "";

    public FolderPickerDialog(IApplication app, IColorTheme theme, string initialPath);
}
```

`IColorTheme` is injected so the dialog can resolve `theme.FileTree`, `theme.Selection`, and `theme.Dialog` at draw time without reaching into `ThemeRegistry`.

### Caller change in AppBootstrap.DoOpenFolder

Replace the existing `OpenDialog` block (lines ~320–330) with:

```csharp
var dlg = new FolderPickerDialog(app, theme, rootDir);
app.Run(dlg);
if (dlg.Canceled) return;
folderPath = dlg.SelectedPath;
```

The try/catch that was added as a crash workaround is removed at the same time.

### Inner type: DirectoryListView

A `private sealed class DirectoryListView : View` inside `FolderPickerDialog`. It holds:

```csharp
private List<string> _entries;  // display names; first entry is ".." when not at root
private int          _selected;
private int          _scrollTop;
private IColorTheme  _theme;

public event EventHandler<string>? EntryActivated; // fires with display name or ".."
```

`_entries` is rebuilt by `Populate(string directoryPath)` which:
1. Calls `Directory.EnumerateDirectories(directoryPath)` wrapped in try/catch (returns empty list on access error)
2. Sorts results case-insensitively by directory name
3. Prepends `".."` unless `directoryPath` is a filesystem root (i.e., `Path.GetPathRoot(directoryPath) == directoryPath`)

`OnDrawingContent` iterates visible rows, sets `theme.Selection` for the selected row and `theme.FileTree` for all others, and writes each entry padded to `Viewport.Width`. Entries that are not `".."` are rendered with a trailing `/` to visually indicate they are directories.

`OnKeyDown` handles: `CursorUp`, `CursorDown`, `Home`, `End`, `PageUp`, `PageDown`, `Enter` (fires `EntryActivated`).

`OnMouseEvent` handles: `LeftButtonClicked` (moves selection, sets focus), `LeftButtonDoubleClicked` (moves selection, fires `EntryActivated`).

`EnsureVisible()` keeps `_scrollTop` in range so the selected row is always visible.

### Initial path fallback

If `initialPath` does not exist at construction time (e.g. a previously-saved session folder was deleted), the dialog silently falls back to `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`. The error label is not shown at open; the user sees a valid starting point rather than an immediate error.

### Windows UNC path edge case

`Path.GetDirectoryName` returns `null` for `\\server\share` (a UNC share root). When `..` is activated and `Path.GetDirectoryName(_currentPath)` returns `null`, the `..` activation is a no-op — `NavigateTo` is not called. `..` is still suppressed when `Path.GetPathRoot(_currentPath) == _currentPath` per the existing rule.

### Navigation logic in FolderPickerDialog

`_currentPath` is a `string` field holding the directory currently displayed.

`NavigateTo(string path)`:
1. Resolves `path` to a full path via `Path.GetFullPath`
2. Verifies `Directory.Exists(path)`; if not, sets the error label text and returns without closing
3. Sets `_currentPath = path`
4. Updates `_pathField.Text`
5. Calls `_listView.Populate(path)`
6. Clears the error label

`DirectoryListView.EntryActivated` is wired to:
```csharp
private void OnEntryActivated(object? sender, string entry)
{
    string next = entry == ".."
        ? Path.GetDirectoryName(_currentPath) ?? _currentPath
        : Path.Combine(_currentPath, entry);
    NavigateTo(next);
    _listView.SetFocus();
}
```

`_pathField` accepts `Enter` via its `Accept` event; when fired, calls `NavigateTo(_pathField.Text)`.

### Select Folder button

`DoAccept()`:
1. Reads `_pathField.Text` trimmed
2. Calls `NavigateTo` to validate (which sets the error label on failure and returns early)
3. If validation passes: sets `SelectedPath = _currentPath`, `Canceled = false`, calls `_app.RequestStop(this)`

### Cancel / Escape

Both set `Canceled = true` (already the default) and call `_app.RequestStop(this)`. The `OnKeyDown` override on the dialog handles `Esc` following the same pattern as `FindInFilesDialog`.

### Layout constants

```csharp
private const int DialogWidth  = 62;
private const int DialogHeight = 18;
private const int ListHeight   = 10;
```

## UI / UX

### Mockup

```
┌─ Open Folder ──────────────────────────────────────────────┐
│                                                            │
│  Path: [/home/user/projects/code-edit___________________]  │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ ..                                                   │  │
│  │ .claude/                                             │  │
│  │ .git/                                                │  │
│  │ assets/                                              │  │
│  │ src/                                                 │  │
│  │ specs/                                               │  │
│  │ tests/                                               │  │
│  │                                                      │  │
│  │                                                      │  │
│  │                                                      │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│                           [ Cancel ]  [ Select Folder ]    │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

Error state (path field + error label):

```
│  Path: [/home/user/does-not-exist_______________________]  │
│  Path not found                                            │
```

The error label sits on the row immediately below the path field. It is a small `View` subclass (`ErrorLabel`) whose `OnDrawingContent` sets the attribute to `theme.StatusBar` colors (which contrast with the dialog background in all built-in themes) and writes the text. This is the same pattern used elsewhere in the project for custom-colored rows. The label is hidden when empty and cleared on the next successful navigation or accepted path.

### Keybindings

| Key | Action |
|---|---|
| Up / Down | Move selection in directory list |
| Home / End | First / last entry in directory list |
| Page Up / Down | Scroll list by one viewport height |
| Enter (list focused) | Activate selected entry (navigate into dir or `..`) |
| Enter (path field focused) | Navigate to the path typed in the field |
| Tab | Cycle focus: path field → directory list → Cancel → Select Folder |
| Shift+Tab | Reverse tab order |
| Enter (Select Folder focused, or IsDefault) | Accept current path |
| Escape | Cancel and close |

### Tab order

Path field → directory list → Cancel button → Select Folder button.

`Select Folder` has `IsDefault = true`, meaning Enter triggers it when no other control intercepts the key. The directory list's `OnKeyDown` explicitly handles Enter and marks it as handled, so the IsDefault button is not triggered while the list is focused.

## Test Cases

Test the dialog's pure logic (navigation, list population, validation) through `FolderPickerDialog` or `DirectoryListView` directly in unit tests, using a real temporary directory structure created with `Directory.CreateTempSubdirectory`.

1. **Initial path display** — constructing the dialog with a valid path populates the path field with that path and populates the list with the immediate subdirectories of that path plus `..` at position 0.

2. **Directories only** — a directory containing both subdirectories and files produces a list whose entries correspond only to subdirectories; no files appear.

3. **Sorted case-insensitively** — subdirectories named `Zebra`, `alpha`, `Beta` appear in the list as `alpha/`, `Beta/`, `Zebra/` (after `..`).

4. **`..` absent at root** — when `initialPath` is a filesystem root (e.g. `/` on Linux), `..` is not the first entry.

5. **Navigate into subdirectory via Enter** — activating a non-`..` entry updates `_currentPath` to the child directory, updates the path field, and repopulates the list with that child's subdirectories.

6. **Navigate to parent via `..`** — activating `..` updates `_currentPath` to the parent, updates the path field, and repopulates the list.

7. **Direct path field edit — valid path** — setting the path field text to an existing directory and firing `Accept` navigates to that directory (updates list and `_currentPath`).

8. **Direct path field edit — nonexistent path** — setting the path field text to a path that does not exist and pressing Select Folder leaves the dialog open and shows a non-empty error label; `Canceled` remains `true` and `SelectedPath` remains empty.

9. **Direct path field edit — path is a file not a directory** — same result as nonexistent path: error label shown, dialog stays open.

10. **Select Folder — happy path** — with a valid `_currentPath`, triggering Select Folder sets `Canceled = false` and `SelectedPath` to `_currentPath`.

11. **Cancel button** — triggering Cancel leaves `Canceled = true` and `SelectedPath` empty.

12. **Escape key** — sending Escape to the dialog's `OnKeyDown` leaves `Canceled = true`.

13. **Access-denied directory** — when `Directory.EnumerateDirectories` throws (simulated by a directory with no read permission), the list is empty except for `..`; no exception propagates.

14. **Scroll state reset on navigation** — after scrolling the list down, navigating into a subdirectory resets `_scrollTop` and `_selected` to 0.

## Open Questions

None.
