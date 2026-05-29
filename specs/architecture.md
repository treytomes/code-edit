# Spec: Overall Architecture

## Status
Implemented

## Overview

code-edit is a TUI code editor built on Terminal.Gui v2, following Clean Architecture. The solution is divided into four projects — **Domain**, **Application**, **Infrastructure**, and **Presentation** — with a strict inward-only dependency rule: inner layers define interfaces; outer layers implement them. The Domain and Application layers have no Terminal.Gui references and are fully unit-testable in isolation. An **event bus** living in the Application layer serves as both the action dispatcher and the undo/redo stack: every mutation to the text buffer is a published `IBufferEvent` carrying its own `Execute` and `Undo` logic. The Infrastructure layer provides a lazy-loading file buffer that indexes line offsets at open time and reads lines on demand, so arbitrarily large files never load fully into memory. All layers are composed via `Microsoft.Extensions.DependencyInjection` and all structured logging flows through `Microsoft.Extensions.Logging` to a rolling file sink — never to the terminal.

## Scope

### In scope
- Solution and project layout (Clean Architecture)
- Layer responsibilities and dependency rules
- Key interface contracts per layer
- DI composition via `Microsoft.Extensions.DependencyInjection`
- Logging system via `Microsoft.Extensions.Logging` (file sink, no terminal output)
- Event bus design (publish, undo/redo, word-level coalescing)
- Lazy file buffer design (line offset index, full structural edit tracking, proportional LRU cache)
- Data flow from keystroke to screen update
- Threading model
- Menu and keybinding integration with the event bus
- Syntax highlighting integration with the editor view
- Language detection strategy (extension → shebang)
- Line ending normalization
- Color theme architecture (`ColorPair` abstraction in Domain, single theme v1, swappable later)
- Error handling strategy at the application boundary

### Out of scope
- File tree panel, multiple tabs/buffers (v2)
- LSP integration (v3)
- Command palette (v3)
- Git integration (v4)
- Plugin or extension system
- Configurable key bindings (v1 ships fixed bindings)

## Design

### Solution Layout

```
code-edit/
├── CodeEdit.sln                   # Solution file at repo root
├── specs/
├── src/
│   ├── CodeEdit.Domain/           # Entities, value objects, core interfaces
│   ├── CodeEdit.Application/      # Use cases, event bus, ports (interfaces for infrastructure)
│   ├── CodeEdit.Infrastructure/   # File buffer, syntax providers, theme, file I/O, logging sink
│   └── CodeEdit.Presentation/     # Terminal.Gui views, menu, status bar, App.cs
├── tests/
│   └── CodeEdit.Tests/            # xUnit — tests Domain and Application only
└── CLAUDE.md
```

### Layer Dependency Rule

```
  CodeEdit.Domain          (no external deps)
       ▲
  CodeEdit.Application     (depends on Domain)
       ▲               ▲
  CodeEdit.Infrastructure  CodeEdit.Presentation
  (depends on Domain       (depends on Application
   + Application)           + Domain)
```

- **Domain** defines entities and interfaces. References nothing outside the BCL.
- **Application** defines use cases, the event bus interface, and ports (interfaces that Infrastructure implements). References Domain only. References `Microsoft.Extensions.Logging.Abstractions` for `ILogger<T>`.
- **Infrastructure** implements ports: `LazyFileBuffer`, `SyntaxProvider` implementations, `ThemeProvider`, rolling file log sink. References Domain and Application.
- **Presentation** wires Terminal.Gui views to Application use cases. References Application and Domain. Does NOT reference Infrastructure directly — Infrastructure is composed at startup via DI.
- **Tests** references Domain and Application only. Zero Terminal.Gui dependency. Zero Infrastructure dependency.

---

### Layer Responsibilities

#### Domain (`CodeEdit.Domain`)
The core model. No dependencies outside the BCL.

- `ITextBuffer` — the read/write interface to a document
- `IBufferEvent` — the contract for a reversible buffer mutation (Execute + Undo)
- Value types: `CursorPosition`, `Selection`, `TextRange`
- `ColorPair` — abstract foreground/background color pair (no Terminal.Gui dependency)
- `TokenType` enum and `SyntaxToken` value type
- `LineEnding` enum (used during save)

#### Application (`CodeEdit.Application`)
Orchestration and ports. Depends on Domain only. References `Microsoft.Extensions.Logging.Abstractions` for `ILogger<T>`.

- `IEventBus` — publishes events, manages undo/redo stack, handles word-level coalescing
- Concrete `IBufferEvent` implementations: `InsertTextEvent`, `DeleteEvent`, `ReplaceEvent`, `MoveCursorEvent`, `SelectEvent`
- `IFileService` — port: open file path → `ITextBuffer`; save `ITextBuffer` → file path
- `ISyntaxDetector` — port: file path + first line → `ISyntaxProvider`
- `IColorTheme` — port: maps `TokenType` → `ColorPair`; provides named color roles using `ColorPair` (background, gutter, selection, etc.) — no Terminal.Gui types
- `ThemeRegistry` — holds the active `IColorTheme`; fires `ThemeChanged` event on swap
- `SearchService` — find/replace logic operating on `ITextBuffer` (pure, no UI)

#### Infrastructure (`CodeEdit.Infrastructure`)
Implements all Application ports. Depends on Domain and Application.

- `LazyFileBuffer` — implements `ITextBuffer` with lazy line loading and full structural edit tracking (see below)
- `FileService` — implements `IFileService`; builds `LazyFileBuffer` on open; streams to temp file on save
- `CSharpSyntaxProvider`, `PlainTextSyntaxProvider`, etc. — implement `ISyntaxProvider`
- `ExtensionShebangSyntaxDetector` — implements `ISyntaxDetector`; checks extension first, then shebang
- `DefaultDarkTheme` — implements `IColorTheme` using `ColorPair`; the single v1 theme
- `RollingFileLoggerProvider` — implements `ILoggerProvider`; writes structured log entries to `~/.code-edit/logs/`

#### Presentation (`CodeEdit.Presentation`)
Terminal.Gui views wired to Application. Depends on Application and Domain.

- `EditorView` — renders buffer lines with syntax colors, scrolling, cursor, selection
- `MenuBarView` — top menu bar (File, Edit, Search, Help)
- `StatusBarView` — bottom bar: file name, cursor position, dirty flag, encoding
- `DialogFactory` — modal dialogs: search/replace, open file, save as, unsaved-changes prompt
- `KeyBindingMap` — maps `Terminal.Gui.Key` → `IBufferEvent` factory
- `ColorPairMapper` — converts `ColorPair` (Domain) to `Terminal.Gui.Attribute` (only class in the codebase that references both)
- `AppBootstrap` (entry point) — composes all layers via MEDI, configures logging, starts `Application.Run`

---

### Key Interfaces

#### Domain

```csharp
interface ITextBuffer
{
    int LineCount { get; }
    string GetLine(int lineIndex);      // lazy — may read from file
    CursorPosition Cursor { get; }
    Selection? Selection { get; }
    bool IsDirty { get; }
    string? FilePath { get; }
    string? DetectedLanguage { get; }
}

interface IBufferEvent
{
    void Execute(IMutableTextBuffer buffer);
    void Undo(IMutableTextBuffer buffer);
    bool TryCoalesce(IBufferEvent next, out IBufferEvent merged);
}

// IMutableTextBuffer extends ITextBuffer with the write surface.
// IEventBus holds IMutableTextBuffer; all read-only consumers receive ITextBuffer.
interface IMutableTextBuffer : ITextBuffer
{
    void InsertText(CursorPosition at, string text);
    void DeleteRange(TextRange range);
    void SetCursor(CursorPosition pos);
    void SetSelection(Selection? selection);
    void ResizeCache(int terminalHeight);
}

// Abstract color pair — no Terminal.Gui reference
readonly record struct ColorPair(int Foreground, int Background);

readonly record struct CursorPosition(int Line, int Column);
readonly record struct Selection(CursorPosition Anchor, CursorPosition Active);
readonly record struct TextRange(CursorPosition Start, CursorPosition End);
readonly record struct SyntaxToken(int Line, int StartColumn, int Length, TokenType Type);

enum TokenType
{
    Default, Keyword, StringLiteral, CharLiteral,
    Comment, Number, Operator, Punctuation, Identifier
}
```

#### Application

```csharp
interface IEventBus
{
    void Publish(IBufferEvent bufferEvent);
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    event EventHandler<BufferEventArgs> EventExecuted;
    event EventHandler<BufferEventArgs> EventUndone;
    event EventHandler<BufferEventArgs> EventRedone;
}

interface ISyntaxProvider
{
    IReadOnlyList<SyntaxToken> Tokenize(IReadOnlyList<string> lines, int startLineIndex);
}

interface ISyntaxDetector
{
    ISyntaxProvider Detect(string? filePath, string? firstLine);
}

// Returns ColorPair only — zero Terminal.Gui coupling in Application or Domain
interface IColorTheme
{
    ColorPair ForToken(TokenType type);
    ColorPair Normal { get; }
    ColorPair Selection { get; }
    ColorPair LineNumber { get; }
    ColorPair StatusBar { get; }
    ColorPair MenuBar { get; }
    ColorPair Dialog { get; }
}

interface IFileService
{
    ITextBuffer Open(string path);
    void Save(ITextBuffer buffer, string path);
    void SaveNew(ITextBuffer buffer, string path);
}
```

#### Presentation (mapping layer)

```csharp
// Single class permitted to reference both ColorPair and Terminal.Gui.Attribute
static class ColorPairMapper
{
    public static Terminal.Gui.Attribute ToAttribute(ColorPair pair) =>
        new Terminal.Gui.Attribute(pair.Foreground, pair.Background);
}
```

---

### Event Bus Design

The `IEventBus` implementation in Application is the central mutation coordinator.

**Publish flow:**
1. Caller creates an `IBufferEvent` and calls `eventBus.Publish(event)`
2. Bus asks the top of the undo stack: `top.TryCoalesce(event, out merged)`
   - If yes: replace top with `merged` (no additional Execute needed — merged carries full state)
   - If no: call `event.Execute(buffer)`, push `event` onto undo stack, clear redo stack
3. Fire `EventExecuted`

**Undo flow:**
1. Pop top of undo stack
2. Call `event.Undo(buffer)`
3. Push onto redo stack
4. Fire `EventUndone`

**Word-level coalescing rule (InsertTextEvent):**
- Two consecutive single-character insertions coalesce if:
  - Both insert at adjacent positions (second char follows first)
  - Neither char is a word boundary (space, tab, punctuation, newline)
- When a word boundary char is inserted, it coalesces with the previous non-boundary run to form one undo step (the word + trailing space/punct)
- Any non-insert event (delete, move, replace) breaks the coalesce chain

```csharp
// Example coalescing in InsertTextEvent
bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
{
    if (next is not InsertTextEvent other) { merged = null!; return false; }
    if (!IsAdjacentInsertion(other)) { merged = null!; return false; }
    if (IsWordBreak(other.Text) && IsWordBreak(this.Text)) { merged = null!; return false; }
    merged = new InsertTextEvent(this.Position, this.Text + other.Text);
    return true;
}
```

---

### Lazy File Buffer Design

`LazyFileBuffer` (Infrastructure) implements `ITextBuffer` without loading the full file into memory.

**Open sequence:**
1. Open a `FileStream` with `FileShare.Read` (kept open for the buffer's lifetime)
2. Scan the stream sequentially, recording the byte offset of each line start into `long[] _physicalOffsets`. Normalize `\r\n` → `\n` during this scan. O(file size) but purely sequential.
3. Build the initial logical line map (see Structural Edit Tracking below)
4. `LineCount` = number of logical lines
5. Detect language via `ISyntaxDetector` (extension first; if unknown, read first line from stream for shebang)

**GetLine(logicalIndex):**
```
1. Resolve logicalIndex to a LogicalLine record via _logicalLines[logicalIndex]
2. If record.IsEdited  → return record.EditedText directly
3. If logicalIndex in  _lineCache → return cached string
4. Seek _fileStream to _physicalOffsets[record.PhysicalIndex]
5. Read bytes to next \n (or EOF), decode UTF-8
6. Store in _lineCache (see LRU Cache below)
7. Return string
```

**Structural Edit Tracking:**

`_logicalLines` is a `List<LogicalLine>` maintained for the lifetime of the buffer:

```csharp
record struct LogicalLine(
    int PhysicalIndex,   // index into _physicalOffsets; -1 for inserted lines
    bool IsEdited,
    string? EditedText   // non-null when IsEdited
);
```

- **Modify a line:** set `IsEdited = true`, `EditedText = newText` on the existing `LogicalLine`
- **Insert a line after index i:** insert a new `LogicalLine(-1, true, newText)` at position `i+1` in `_logicalLines`
- **Delete a line at index i:** remove `_logicalLines[i]`
- `LineCount` = `_logicalLines.Count`
- `IsDirty` = `_logicalLines.Any(l => l.IsEdited || l.PhysicalIndex == -1)` or any deletions have occurred (tracked by a `bool _hasStructuralEdits` flag)

This approach gives O(1) lookup for edited lines and O(n) for insertions/deletions where n is the number of logical lines — acceptable for v1 because structural edits are infrequent relative to single-character inserts.

**LRU Cache:**

- Capacity = `4 × terminalHeight` lines, where `terminalHeight` is provided at construction and updated via `ResizeCache(int newTerminalHeight)` when the terminal is resized
- Eviction: least-recently-used entry is dropped when capacity is exceeded
- Only unedited lines from the physical file are cached; edited lines are stored in `_logicalLines` directly and are never cached
- Cache is invalidated for a logical index when that index transitions to `IsEdited = true`

**Save:**
1. Allocate a temp path adjacent to the target: `targetPath + ".tmp"`
2. Open a `FileStream` for writing to the temp path
3. Iterate `_logicalLines` 0..Count-1:
   - If `IsEdited`: write `EditedText + "\n"` encoded as UTF-8
   - Else: copy the raw bytes from `_fileStream` at `_physicalOffsets[PhysicalIndex]` to the next `\n`
4. Flush and close the temp stream
5. `File.Replace(tempPath, targetPath, null)` (atomic rename; no backup)
6. Re-scan the saved file to rebuild `_physicalOffsets` and reset all `LogicalLine` records to unedited

**Disposal:**
- `LazyFileBuffer` implements `IDisposable`; `Dispose()` closes `_fileStream` and clears `_logicalLines`

---

### Language Detection

`ExtensionShebangSyntaxDetector.Detect(filePath, firstLine)`:

1. If `filePath` is not null: check extension against a static map (`".cs"` → C#, `".py"` → Python, etc.)
2. If extension is unknown or `filePath` is null: check `firstLine` for a shebang (`#!`)
   - Parse the interpreter name from the shebang line
   - Map interpreter to language (`python`, `python3` → Python; `bash`, `sh` → Shell; etc.)
3. If still undetected: return `PlainTextSyntaxProvider`

---

### Color Theme Architecture

`IColorTheme` is an Application-layer port that returns `ColorPair` (a Domain value type). There is no Terminal.Gui type anywhere in Domain or Application. The sole class permitted to bridge `ColorPair` → `Terminal.Gui.Attribute` is `ColorPairMapper` in Presentation.

```csharp
// Application layer — no Terminal.Gui reference
class ThemeRegistry
{
    public IColorTheme Active { get; private set; }
    public event EventHandler? ThemeChanged;

    public void SetTheme(IColorTheme theme)
    {
        Active = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

`ThemeRegistry` is registered as a singleton in MEDI and injected into views. When `ThemeChanged` fires, views call `SetNeedsDisplay()`. In v1, `SetTheme` is never called after startup — it exists to make future configurability a one-line change.

`DefaultDarkTheme` (Infrastructure) implements `IColorTheme` using `ColorPair` with integer color indices compatible with Terminal.Gui's `Color` enum values — the mapping table lives only in `ColorPairMapper`.

---

### Logging System

All logging uses `Microsoft.Extensions.Logging` (`ILogger<T>`). No layer writes directly to the console or terminal — doing so would corrupt the TUI display.

**Log sink:** `RollingFileLoggerProvider` in Infrastructure writes to `~/.code-edit/logs/`. Files roll daily:
```
~/.code-edit/logs/
├── codeedit-2026-05-27.log
├── codeedit-2026-05-26.log
└── ...
```

Files older than 14 days are deleted on startup. Each log line is structured plain text:
```
2026-05-27T14:32:01.123Z [INF] FileService       Opening file: /home/user/foo.cs (size: 1204831 bytes)
2026-05-27T14:32:01.145Z [INF] LazyFileBuffer    Indexed 4821 lines in 22ms
2026-05-27T14:32:05.001Z [WRN] EventBus          Undo stack empty — ignoring Undo request
2026-05-27T14:32:10.444Z [ERR] AppBootstrap      Unhandled exception
  System.UnauthorizedAccessException: Access to path '/etc/shadow' denied.
    at CodeEdit.Infrastructure.FileService.Open(String path) ...
```

**Log levels:**
- `Trace` — per-keystroke events, cache hits/misses (disabled in release builds)
- `Debug` — buffer mutations, coalescing decisions, syntax tokenization timing
- `Information` — file open/save, theme changes, application start/stop
- `Warning` — recoverable anomalies (undo stack empty, unknown file extension, etc.)
- `Error` — caught exceptions that degrade functionality
- `Critical` — unhandled exceptions before exit

**Configuration in `AppBootstrap`:**

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Debug)
        .AddProvider(new RollingFileLoggerProvider(
            logDirectory: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".code-edit", "logs")));
});
```

`ILoggerFactory` is registered in the MEDI container. Services that need logging accept `ILogger<T>` via constructor injection.

---

### DI Composition

`AppBootstrap` builds the MEDI container before starting Terminal.Gui:

```csharp
var services = new ServiceCollection();

// Logging
services.AddLogging(b => b.AddProvider(new RollingFileLoggerProvider(logDir)));

// Domain (no registrations — pure value types and interfaces)

// Application
services.AddSingleton<IEventBus, EventBus>();
services.AddSingleton<ThemeRegistry>();
services.AddSingleton<SearchService>();

// Infrastructure
services.AddSingleton<IFileService, FileService>();
services.AddSingleton<ISyntaxDetector, ExtensionShebangSyntaxDetector>();
services.AddSingleton<IColorTheme, DefaultDarkTheme>();

// Presentation
services.AddSingleton<EditorView>();
services.AddSingleton<MenuBarView>();
services.AddSingleton<StatusBarView>();
services.AddSingleton<DialogFactory>();
services.AddSingleton<KeyBindingMap>();

var provider = services.BuildServiceProvider();
```

`ThemeRegistry` is seeded with `DefaultDarkTheme` immediately after container build. Infrastructure is never referenced by Presentation directly — both are resolved through the container.

---

### Data Flow: Keystroke to Screen Update

```
User presses a key
        │
        ▼
Terminal.Gui raises KeyDown on EditorView (Presentation)
        │
        ▼
KeyBindingMap.Resolve(key) → IBufferEvent factory (Application)
        │
   mapped?
   ├─ Yes ─► Construct IBufferEvent, call EventBus.Publish(event)
   │
   └─ No, printable char ─► Construct InsertTextEvent, call EventBus.Publish(event)
                                    │
              ┌─────────────────────┘
              ▼
        EventBus.Publish (Application)
              │
              ├── TryCoalesce with undo stack top?
              │     Yes: replace top (no Execute)
              │     No:  event.Execute(buffer) → mutates LazyFileBuffer
              │
              ├── Push to undo stack
              │
              └── Fire EventExecuted
                        │
                        ▼
              EditorView.OnEventExecuted (Presentation)
                        │
                        ▼
              EditorView.SetNeedsDisplay()
                        │
                        ▼
              Terminal.Gui redraws on next frame
                        │
                        ▼
              EditorView.Draw() called by framework
                        │
                        ├── For each visible line: ITextBuffer.GetLine(i) → lazy load
                        ├── ISyntaxProvider.Tokenize(visibleLines, startLine)
                        ├── IColorTheme.ForToken(type) → ColorPair
                        └── ColorPairMapper.ToAttribute(pair) → Terminal.Gui.Attribute
```

---

### Threading Model

Terminal.Gui v2 is single-threaded. All view rendering and event handling runs on the main thread.

- **Main thread:** all Terminal.Gui operations, all `IEventBus.Publish` calls, all `ITextBuffer` mutations
- **Background threads:** none in v1. File I/O (open/save) runs synchronously on the main thread. The lazy-loading design means `Open` is fast (scan only); only `GetLine` seeks, and seeks are sub-millisecond for typical files.
- **Rule:** any future background work must dispatch back to the main thread via `Application.Invoke(Action)` before touching any view or buffer state.

---

### Menu Integration

The `MenuBar` is a Terminal.Gui `MenuBar`. Each `MenuItem` calls a method on a thin `MenuController` (Presentation) which constructs the appropriate `IBufferEvent` and calls `EventBus.Publish`. `CanExecute` conditions (e.g., "Save" disabled when not dirty) are evaluated when the menu opens by checking buffer state directly.

Keybindings and menu items invoke the same event construction path — there is no duplication of logic.

---

### Error Handling Strategy

- **File I/O errors** (not found, permission denied, disk full): caught in `FileService`; re-thrown as `FileServiceException` (Application layer type); caught in Presentation and shown as a Terminal.Gui `MessageBox`.
- **Buffer logic errors** (cursor out of bounds, invalid range): `InvalidOperationException` with a clear message. Caught by tests; never expected at runtime.
- **Unhandled exceptions**: caught in `AppBootstrap` top-level handler; logged to `~/.code-edit/error.log`; brief error dialog shown before clean exit.

## UI / UX

### Application Layout

```
┌─────────────────────────────────────────────────────────────┐
│ File   Edit   Search   Help                                  │  ← MenuBar (height 1)
├─────────────────────────────────────────────────────────────┤
│   1 │ using System;                                          │
│   2 │                                                        │
│   3 │ namespace CodeEdit;                                    │  ← EditorView
│   4 │                                                        │     Dim.Fill() both axes
│   5 │ class Program                                          │
│   6 │ {                                                      │
│   7 │     static void Main() { }                            │
│   8 │ }                                                      │
│     │                                                        │
├─────────────────────────────────────────────────────────────┤
│ Program.cs        Ln 7, Col 24      UTF-8    [Modified]      │  ← StatusBar (height 1)
└─────────────────────────────────────────────────────────────┘
```

- `MenuBar` pinned to top (height 1)
- `StatusBar` pinned to bottom (height 1)
- `EditorView` fills remaining space with `Dim.Fill()`
- Line number gutter is a fixed-width column rendered inside `EditorView`

## Verification

When the architecture is correctly realized:

1. `dotnet build src/CodeEdit.sln` succeeds with zero warnings
2. `dotnet test src/CodeEdit.Tests` passes with all tests green
3. `CodeEdit.Tests` has zero `Terminal.Gui` references: `grep -r "Terminal.Gui" src/CodeEdit.Tests` → no matches
4. `CodeEdit.Domain` has zero `Terminal.Gui` references: same check
5. `CodeEdit.Application` has zero `Terminal.Gui` references: same check
6. `CodeEdit.Infrastructure` has zero `Terminal.Gui` references: same check
7. `Terminal.Gui` references in `CodeEdit.Presentation` appear only in `ColorPairMapper`, views, and `AppBootstrap`
8. Log file is created at `~/.code-edit/logs/` on first run; no log output appears on the terminal
9. The application launches, displays the layout above, and accepts keystrokes

## Implementation Notes

### Terminal.Gui v2 namespace layout
Discovered during scaffolding — v2 uses sub-namespaces, not flat `Terminal.Gui`:

| Type | Namespace |
|---|---|
| `Application` | `Terminal.Gui.App` |
| `IApplication`, `IRunnable` | `Terminal.Gui.App` |
| `View` | `Terminal.Gui.ViewBase` |
| `Window`, `Dialog`, `ListView`, etc. | `Terminal.Gui.Views` |
| `Attribute`, `Color` | `Terminal.Gui.Drawing` |

`CodeEdit.Application` and `Terminal.Gui.App.Application` collide when both are in scope.
Use `using TGuiApp = Terminal.Gui.App.Application;` in any file that needs both.

### Terminal.Gui v2 application lifecycle
The old static `Application.Init/Run/Shutdown` is marked `[Obsolete]`. The correct v2 pattern:
```csharp
var app = TGuiApp.Create();
app.Init();
app.Run(window);     // blocks; stop with app.RequestStop()
```

## Open Questions

None — all design questions resolved.
