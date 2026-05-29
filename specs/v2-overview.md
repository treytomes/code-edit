# v2 Overview

## Status
In Progress

## Features
- File tree panel (toggleable, left-side)
- Multiple tabs / buffers with per-tab undo/redo
- Session persistence (restore open tabs on launch)
- Open File / Open Folder split; recent folders in recent items list

## Implementation Order

The five v2 specs must be implemented in this sequence:

| Step | Spec | Reason |
|------|------|--------|
| 1 | `event-history.md` | Extracts `EventHistory` from `EventBus`; prerequisite for per-tab undo/redo |
| 2 | `buffer-manager.md` | Builds multi-buffer infrastructure on top of `EventHistory`; all UI specs depend on it |
| 3 | `tabs-ui.md` | Adds `TabBarView` and tab keyboard shortcuts; makes the multi-buffer model visible |
| 4 | `session.md` | Persists open tabs to `.code-edit/session.json`; depends on `BufferManager` being stable |
| 5 | `file-tree.md` | Adds `FileTreeView`, Open Folder, and recent folders; depends on tabs working (opens files as new tabs) |

Steps 3–5 have no dependency on each other and could be reordered, but the sequence above is recommended.
