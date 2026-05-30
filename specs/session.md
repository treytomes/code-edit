# Spec: Session Persistence

## Status
Implemented

## Overview
Persist the list of open files and the active tab index to `.code-edit/session.json` in the working directory. On next launch, restore those tabs automatically. Prerequisite: `buffer-manager.md`.

## Scope

**In scope**
- `SessionService` in `CodeEdit.Infrastructure.Settings`
- Save triggered whenever tabs change: file opened, tab closed, active tab changed
- Restore on launch: open saved files as tabs, activate the previously active tab
- Missing files silently skipped on restore
- Unit tests for `SessionService`

**Out of scope**
- Prompting the user to add `.code-edit/session.json` to `.gitignore` (the user decides)
- Saving scroll position or cursor position per tab (v3+)
- Untitled (unsaved) buffers are not persisted

## Design

### Session file

Location: `{workingDir}/.code-edit/session.json`

`workingDir` is determined the same way as the file tree root (file-tree.md):
1. Parent directory of the command-line file argument, if provided.
2. Otherwise `Environment.CurrentDirectory`.

The `.code-edit/` directory is created if it doesn't exist.

### `SessionData`

```csharp
public sealed record SessionData(
    IReadOnlyList<string> OpenFiles,   // absolute paths; Untitled buffers omitted
    int ActiveIndex,                   // index into OpenFiles; clamped to valid range on load
    string? RootDir = null);           // working directory root; null means use default
```

JSON representation:
```json
{
  "openFiles": ["/home/trey/project/src/Program.cs", "/home/trey/project/README.md"],
  "activeIndex": 0,
  "rootDir": "/home/trey/project"
}
```

### `SessionService`

```csharp
public sealed class SessionService(ILogger<SessionService> logger, string? workingDir = null)
{
    public SessionData Load();   // returns empty SessionData if file absent or malformed
    public void Save(SessionData data);
}
```

- `workingDir` defaults to `Environment.CurrentDirectory`; the test-isolation pattern matches `SettingsService` and `RecentFilesService`.
- `Load()` logs a warning and returns `new SessionData([], 0)` if the file is malformed JSON.
- `Save()` writes atomically (write to `.tmp`, then rename) to avoid corruption on crash.

### `AppBootstrap` integration

**On launch** (after `BufferManager` is initialised with one `EmptyBuffer`):
1. Call `sessionService.Load()`.
2. For each path in `OpenFiles` that exists on disk, call `fileService.Open(path)` and `bufferManager.Add(buffer)`. Paths that no longer exist are silently skipped.
3. If at least one file was successfully restored, close the placeholder `EmptyBuffer` at index 0. This must happen *after* step 2 — `BufferManager.Close` throws if it is the last tab, so the placeholder can only be removed once there is at least one other tab.
4. Activate the saved `ActiveIndex`, clamped to `[0, restoredCount - 1]` where `restoredCount` is the number of files actually opened in step 2 (not `OpenFiles.Count`, which may be larger if some files were skipped).

**On change**: subscribe to `BufferManager.TabsChanged` and `BufferManager.ActiveTabChanged`; call `sessionService.Save(BuildSessionData())` in both handlers. Also save whenever the root directory changes (Open Folder).

```csharp
SessionData BuildSessionData() => new(
    eventBus.Buffers.Tabs
        .Select(t => t.Buffer.FilePath)
        .Where(p => p is not null)
        .ToList()!,
    eventBus.Buffers.ActiveIndex,
    _rootDir);   // current working directory root tracked in AppBootstrap
```

### Tests

- Save and reload round-trips correctly (including `RootDir`).
- Missing files on restore are skipped without error.
- Malformed JSON returns empty `SessionData`.
- `ActiveIndex` is clamped to the number of successfully restored files, not the raw saved value.
- `workingDir` isolation (temp directory) matches existing infrastructure test pattern.

## Open Questions
None.
