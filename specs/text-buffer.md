# Spec: Text Buffer

## Status
Implemented

## Overview

The text buffer is the authoritative mutable document state for code-edit. It stores the contents of an open file, exposes lines for rendering, tracks cursor position and selection, and records whether the document has unsaved changes. For v1 the buffer is always backed by a file on disk; there is no concept of an "untitled" new document. The `LazyFileBuffer` implementation in Infrastructure opens a file by scanning line offsets and reads lines on demand, so arbitrarily large files are supported without loading them fully into memory.

## Scope

### In scope
- `ITextBuffer` interface (Domain) — line access, cursor, selection, dirty flag, file path, detected language
- `LogicalLine` value type (Infrastructure) — the per-line record used internally by `LazyFileBuffer`
- `LazyFileBuffer` (Infrastructure) — file-backed `ITextBuffer` with lazy line loading, structural edit tracking, LRU cache, and atomic save
- `FileService` (Infrastructure) — `IFileService` implementation that constructs `LazyFileBuffer`
- `FileServiceException` (Application) — typed exception for I/O errors surfaced to Presentation
- Line ending normalisation: `\r\n` and `\r` → `\n` on open; `\n` on save
- Single-character and multi-character line mutation (insert, delete, replace within a line)
- Line insertion and deletion (newline, backspace-at-line-start)
- Cursor movement (by character, by line, start/end of line, start/end of document)
- Selection (anchor + active cursor)
- Dirty flag management
- Atomic save (temp file + rename)
- LRU line cache sized at 4 × terminal height, recalculated on resize

### Out of scope
- Undo/redo (belongs to the event bus spec)
- `IBufferEvent` implementations (belongs to the event bus spec)
- Syntax detection (belongs to the syntax highlighting spec)
- Clipboard (belongs to the edit commands spec)
- Multiple open buffers (v2)
- New untitled documents (v1 always opens a file)

## Design

### Domain types

`ITextBuffer` remains read-only — cursor, selection, line access, and metadata only. A separate `IMutableTextBuffer` interface (also in Domain) extends it with the mutation surface. This keeps the read contract clean for consumers (renderers, syntax providers, status bar) while making writability explicit for the event bus.

```csharp
// Domain — read-only consumer contract (already defined, no changes)
public interface ITextBuffer
{
    int LineCount { get; }
    string GetLine(int lineIndex);
    CursorPosition Cursor { get; }
    Selection? Selection { get; }
    bool IsDirty { get; }
    string? FilePath { get; }
    string? DetectedLanguage { get; }
}

// Domain — write contract, extends ITextBuffer
public interface IMutableTextBuffer : ITextBuffer
{
    void InsertText(CursorPosition at, string text);
    void DeleteRange(TextRange range);
    void SetCursor(CursorPosition pos);
    void SetSelection(Selection? selection);
    void ResizeCache(int terminalHeight);
}
```

`IBufferEvent.Execute` and `IBufferEvent.Undo` accept `IMutableTextBuffer`. `IEventBus` holds an `IMutableTextBuffer`. All consumers that only read (views, syntax, status bar) receive `ITextBuffer`.

`CursorPosition`, `Selection`, and `TextRange` are already defined in Domain — no changes.

### `FileServiceException` (Application)

```csharp
// Application/Ports/FileServiceException.cs
public sealed class FileServiceException(string message, Exception? inner = null)
    : Exception(message, inner);
```

Thrown by `FileService` for all I/O errors. Caught in Presentation and shown as a `MessageBox`. Never thrown from within `LazyFileBuffer` itself — only from `FileService.Open` and `FileService.Save*`.

---

### `LogicalLine` (Infrastructure, internal)

```csharp
internal record struct LogicalLine(
    int    PhysicalIndex,  // index into _physicalOffsets; -1 for inserted lines
    bool   IsEdited,
    string EditedText      // non-null when IsEdited; empty string for blank inserted lines
);
```

`_logicalLines` is a `List<LogicalLine>` maintained for the buffer's lifetime. It is the only mutable data structure that tracks document content; `_physicalOffsets` is immutable after open.

---

### `LazyFileBuffer` (Infrastructure)

#### Fields

```csharp
private readonly string _filePath;
private readonly FileStream _fileStream;        // kept open; FileShare.Read
private readonly long[] _physicalOffsets;       // byte offset of each line in the file
private readonly List<LogicalLine> _logicalLines;
private readonly LruCache<int, string> _lineCache;
private CursorPosition _cursor;
private Selection? _selection;
private bool _hasStructuralEdits;
private string? _detectedLanguage;
```

#### Open sequence (`FileService.Open`)

1. Open `FileStream` with `FileAccess.Read`, `FileShare.Read`
2. Scan stream byte-by-byte, recording the start offset of each line into a `List<long>`. Normalise `\r\n` → `\n` logically during scan (record offset of char after `\r`). Convert to `long[]`.
3. Build `_logicalLines`: one `LogicalLine(physicalIndex: i, IsEdited: false, EditedText: "")` per line.
4. If file is empty, seed with one logical line representing an empty document.
5. Construct `LruCache` with initial capacity `4 × Console.WindowHeight` (updated later via `ResizeCache`).
6. Language detection deferred to the syntax spec — `DetectedLanguage` returns `null` for now.

#### `GetLine(int logicalIndex)`

```
Validate: 0 ≤ logicalIndex < LineCount → throw ArgumentOutOfRangeException otherwise
If _logicalLines[logicalIndex].IsEdited  → return EditedText
If _lineCache.TryGet(logicalIndex, out var cached) → return cached
Seek _fileStream to _physicalOffsets[physicalIndex]
Read bytes until '\n' or EOF, decode UTF-8
Store in _lineCache keyed by logicalIndex
Return string
```

The physical bytes may contain `\r` (from `\r\n` or standalone `\r`); strip any trailing `\r` after reading.

#### Mutations

All mutations are invoked by `IBufferEvent.Execute` (event bus spec), which receives `IMutableTextBuffer`. `LazyFileBuffer` implements `IMutableTextBuffer`, so no downcast is needed for mutation.

**InsertText** handles two cases:
- Text contains no `\n`: mutate `_logicalLines[at.Line]` in place (set `IsEdited = true`, splice `text` into `EditedText` at `at.Column`). Invalidate cache entry.
- Text contains `\n`: split into segments, replace the target logical line with the first segment, insert new `LogicalLine` entries for subsequent segments. Set `_hasStructuralEdits = true`. Invalidate cache entries at and above `at.Line`.

**DeleteRange** handles:
- Range within one line: splice out the column range from `EditedText` (or materialise from file first). Invalidate cache entry.
- Range spanning multiple lines: collapse to a single line (join first-line prefix + last-line suffix), remove intermediate logical lines. Set `_hasStructuralEdits = true`.

**SetCursor**: set `_cursor`. Clamp to valid range.

**SetSelection**: set `_selection`. `null` clears selection.

#### `IsDirty`

```csharp
public bool IsDirty => _hasStructuralEdits || _logicalLines.Any(l => l.IsEdited);
```

#### Save (`FileService.Save` / `FileService.SaveNew`)

1. Determine temp path: `targetPath + ".tmp"`
2. Open `FileStream` for write to temp path
3. Iterate `_logicalLines`:
   - `IsEdited` or `PhysicalIndex == -1` → write `EditedText + '\n'` as UTF-8
   - Otherwise → seek `_fileStream`, copy bytes to next `\n` or EOF into temp stream
4. Flush and close temp stream
5. `File.Replace(tempPath, targetPath, null)` — atomic rename, no backup file
6. Re-scan the saved file: rebuild `_physicalOffsets`, reset all `_logicalLines` to unedited, clear `_hasStructuralEdits`, clear `_lineCache`

#### LRU Cache

`LruCache<int, string>` is a simple internal class:
- Backed by `Dictionary<int, LinkedListNode<(int key, string value)>>` + `LinkedList`
- Capacity = `4 × terminalHeight`
- `TryGet`: move node to head, return value
- `Put`: add to head; if over capacity, remove tail node and its dictionary entry
- `Invalidate(int key)`: remove specific key if present
- `Clear()`: remove all entries

#### Disposal

`LazyFileBuffer` implements `IDisposable`. `Dispose()` closes `_fileStream` and clears `_logicalLines`.

---

### `FileService` (Infrastructure)

```csharp
public sealed class FileService(
    ISyntaxDetector syntaxDetector,
    ILogger<FileService> logger) : IFileService
```

**Open:**
1. Log `Information` with file path and size
2. Wrap in try/catch; rethrow I/O exceptions as `FileServiceException`
3. Construct and return `LazyFileBuffer`

**Save / SaveNew:**
1. Log `Information`
2. Downcast `ITextBuffer` to `LazyFileBuffer` — if cast fails, throw `InvalidOperationException` (should never happen in practice)
3. Call `LazyFileBuffer`'s internal save sequence
4. Wrap I/O exceptions as `FileServiceException`

---

### Cursor movement rules

All cursor movements are implemented as `IBufferEvent` subclasses (event bus spec), but the rules are defined here since they depend on buffer structure:

| Move | Rule |
|---|---|
| Right | Column + 1; if past line end, move to column 0 of next line (if exists) |
| Left | Column − 1; if before line start, move to end of previous line (if exists) |
| Down | Line + 1, clamp column to new line length |
| Up | Line − 1, clamp column to new line length |
| Line start | Column = 0 |
| Line end | Column = `GetLine(line).Length` |
| Doc start | Line = 0, Column = 0 |
| Doc end | Line = `LineCount − 1`, Column = last line length |

"Past line end" means column > `GetLine(line).Length`. Column = `GetLine(line).Length` is valid (cursor after last char, before newline).

---

### Selection rules

- Selection begins when a movement key is pressed with Shift held (event bus responsibility to construct `SelectEvent`)
- `Selection.Anchor` is set once when selection begins and does not move until selection is cleared
- `Selection.Active` tracks the moving end
- Selection is cleared (set to `null`) on any non-shift movement or text mutation
- An `InsertText` or `DeleteRange` that targets a non-null selection replaces the selected range first

## UI / UX

The buffer itself has no UI. `EditorView` consumes `ITextBuffer` for rendering — that is covered in the editor view spec. The status bar reads `IsDirty`, `FilePath`, and `Cursor` — covered in the status bar spec.

## Test Cases

All tests live in `tests/CodeEdit.Tests/`. Test class: `LazyFileBufferTests`.

### Open and line access
1. Open a single-line file → `LineCount == 1`, `GetLine(0)` returns content without trailing newline
2. Open a multi-line file → `LineCount` matches actual line count
3. Open an empty file → `LineCount == 1`, `GetLine(0) == ""`
4. Open a file with `\r\n` endings → lines returned without `\r`
5. Open a file with `\r` endings → lines returned without `\r`
6. `GetLine` with out-of-range index → throws `ArgumentOutOfRangeException`

### Mutations
7. `InsertText` within a line → line content updated, `IsDirty == true`
8. `InsertText` with `\n` in text → `LineCount` increases, content split correctly
9. `DeleteRange` within a line → content updated
10. `DeleteRange` spanning multiple lines → lines joined, `LineCount` decreases
11. After mutation, `GetLine` returns updated content without re-reading from file

### Cursor
12. `SetCursor` to valid position → `Cursor` reflects new position
13. `SetCursor` clamps column to line length if column exceeds it

### Selection
14. `SetSelection` to a range → `Selection` reflects it
15. `SetSelection(null)` → `Selection == null`

### Dirty flag
16. Fresh open → `IsDirty == false`
17. Any mutation → `IsDirty == true`
18. After save → `IsDirty == false`

### Save
19. Save writes all lines with `\n` endings
20. Save with structural edits (line inserted) → saved file has correct line count
21. After save, buffer is no longer dirty and `GetLine` reads from new file state

### LRU cache
22. `GetLine` for the same index twice → second call returns from cache (verifiable by closing the file stream externally and confirming no I/O error on second call — or by subclassing for test inspection)
23. `ResizeCache` with smaller capacity → evicts excess entries

### FileService
24. `Open` on a non-existent path → throws `FileServiceException`
25. `Open` on a file with no read permission → throws `FileServiceException`

## Open Questions

None — all design questions resolved.

## Implementation Notes

### `FileService.Save` downcast
`Save(ITextBuffer)` downcasts to `LazyFileBuffer` (or `IMutableTextBuffer`) to access the internal save sequence. This is safe in v1 — `LazyFileBuffer` is the only implementation. An alternative would be to not route saves through `IFileService` at all and call `LazyFileBuffer.Save(string path)` directly, but keeping all file operations behind `IFileService` is the cleaner abstraction for now.

**Revisit when:** a second `ITextBuffer` implementation exists, or when the save path becomes meaningfully more complex (e.g. remote files, watched files). At that point the downcast will become obviously wrong and the right split will be clear.
