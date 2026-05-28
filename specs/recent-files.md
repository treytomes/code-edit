# Spec: Recent Files

## Status
Implemented

## Overview
Track the last N files opened or saved and display them in a "Open Recent" submenu
under File. Selecting an entry opens that file with the same unsaved-changes guard
as File > Open.

## Scope

**In scope**
- Persisting a capped list of recently accessed paths to `~/.code-edit/recent.json`
- "Open Recent ▶" submenu in the File menu, populated at menu-open time
- Entries appear most-recent-first; the list is capped at `recentFilesMax` (see settings)
- Selecting an entry runs the same unsaved-changes guard and open logic as `DoOpen`
- The path is added/promoted to the front of the list whenever a file is opened
  (via File > Open, command-line argument) or saved (File > Save / Save As)
- "Clear Recent Files" item at the bottom of the submenu
- If the list is empty, the submenu shows a single disabled "(empty)" item
- Paths are stored as absolute paths

**Out of scope**
- Pinning entries
- Showing the file's last-modified time or preview
- Per-project recent lists
- Watching for files being deleted/renamed after they are recorded

## Design

### RecentFilesService (Infrastructure)

New class in `CodeEdit.Infrastructure.Settings`:

```csharp
public sealed class RecentFilesService(ILogger<RecentFilesService> logger, EditorSettings settings)
```

Persists to `~/.code-edit/recent.json` — a JSON array of absolute path strings,
most-recent-first, capped at `EditorSettings.RecentFilesMax`.

```jsonc
[
  "/home/alice/projects/foo/main.cs",
  "/home/alice/notes.md"
]
```

Public API:

```csharp
IReadOnlyList<string> Load();          // returns [] on missing/corrupt file
void Add(string path);                 // prepend, deduplicate, cap, then save
void Clear();                          // write empty array
```

`Add` normalises the path to an absolute path before storing. Duplicate entries
(case-sensitive on Linux/macOS, case-insensitive on Windows) are removed before
prepending. Save failures are logged as warnings and swallowed.

### AppBootstrap wiring

Register `RecentFilesService` as a singleton. Resolve it alongside `fileService`.

Call `recentFiles.Add(path)` after every successful open or save:
- After `fileService.Open(path)` succeeds
- After `fileService.Save(buf)` succeeds (using `buf.FilePath`)
- After `fileService.SaveAs(...)` succeeds (using the new path)
- After the command-line argument path is opened successfully

### File menu layout

```
File
  New            Ctrl+N
  Open…          Ctrl+O
  Open Recent ▶
  ────────────────────
  Save           Ctrl+S
  Save As…
  ────────────────────
  Exit
```

The "Open Recent" submenu is a `MenuBarItem` whose children are rebuilt each time
the menu bar is opened. Terminal.Gui v2 supports lazily-evaluated menu children
via a `Func<MenuItem[]>` overload of `MenuBarItem`; use that to call
`BuildRecentMenuItems()` on demand.

`BuildRecentMenuItems()`:

```csharp
MenuItem[] BuildRecentMenuItems()
{
    var paths = recentFiles.Load();
    if (paths.Count == 0)
        return [new MenuItem("(empty)", "", null) { CanExecute = () => false }];

    var items = new List<MenuItem>();
    foreach (var path in paths)
        items.Add(new MenuItem(ShortenPath(path), "", () => DoOpenRecent(path)));
    items.Add(null!);  // divider
    items.Add(new MenuItem("Clear Recent Files", "", () => recentFiles.Clear()));
    return [.. items];
}
```

`ShortenPath` replaces the home directory prefix with `~` for display only.

`DoOpenRecent(path)` runs the unsaved-changes guard then opens the file, identical
to `DoOpen` except the path is already known:

```csharp
void DoOpenRecent(string path)
{
    if (eventBus.Buffer.IsDirty)
    {
        var choice = MessageBox.Query(app, "Unsaved Changes",
            "You have unsaved changes.\nOpen a new file anyway?", "Yes", "No");
        if (choice != 0) return;
    }
    try
    {
        SetActiveBuffer((IMutableTextBuffer)fileService.Open(path));
        recentFiles.Add(path);
    }
    catch (FileServiceException ex)
    {
        statusBar.SetMessage(ex.Message);
    }
}
```

If the file no longer exists, `fileService.Open` throws `FileServiceException` and
the error surfaces in the status bar. The entry is not removed from the list
automatically (user can "Clear Recent Files" if desired).

## File layout

```
src/CodeEdit.Infrastructure/
  Settings/
    RecentFilesService.cs   — NEW

src/CodeEdit.Presentation/
  AppBootstrap.cs           — CHANGED: register service, wire Add calls,
                              add "Open Recent" submenu to File menu
```

## UI / UX

```
┌─ File ──────────────────────────┐
│ New              Ctrl+N         │
│ Open…            Ctrl+O         │
│ Open Recent    ▶ ─────────────┐ │
│ ─────────────────│ ~/foo.cs   │ │
│ Save             │ ~/bar.md   │ │
│ Save As…         │ ─────────  │ │
│ ─────────────────│ Clear …    │ │
│ Exit             └────────────┘ │
└─────────────────────────────────┘
```

Empty state:

```
│ Open Recent    ▶ ─────────────┐ │
│                 │ (empty)     │ │
│                 └─────────────┘ │
```

## Open Questions

None.
