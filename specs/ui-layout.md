# Spec: UI Layout

## Status
Implemented

## Overview

The UI layout spec defines how `AppBootstrap` wires the main window together: an `EditorView` fills the available space, a `StatusBarView` occupies the bottom row, and a `MenuBar` sits at the top. At startup, if a file path is provided on the command line, it is opened immediately and set on the event bus. Otherwise the editor shows a blank writable buffer.

## Scope

### In scope
- `AppBootstrap` main window layout: `MenuBar` (top), `EditorView` (fill), `StatusBarView` (bottom 1 row)
- Command-line argument: optional file path (`args[0]`)
- Startup buffer: if a path is given, open it via `IFileService.Open` and call `IEventBus.SetBuffer` + `EditorView.SetBuffer`; otherwise create and set an `EmptyBuffer`
- `EmptyBuffer` — a minimal `IMutableTextBuffer` used when no file is open; single empty line, writable, `FilePath = null`
- `StatusBarView` displays: current file path (or `[No File]`), cursor position (`Ln X, Col Y`), dirty indicator (`*` when `IsDirty`)
- `MenuBar` — `File` menu with placeholder items (no-ops for v1): `Open`, `Save`, `Quit`

### Out of scope
- Functional File > Open dialog (future file-open spec)
- Functional File > Save (future file-save spec)
- Edit / Search menus (future specs)
- Tabs / multiple buffers (v2)

## Design

### Layout

```
┌──────── code-edit ──────────────────────────────────┐
│ File                                                 │  ← MenuBar (row 0, fills width)
├──────────────────────────────────────────────────────┤
│   1 fn main() {                                      │
│   2     println!("hello");                           │  ← EditorView (fills remaining)
│   3 }                                                │
│   4 ▌                                                │
│                                                      │
├──────────────────────────────────────────────────────┤
│ src/main.rs                    Ln 4, Col 1           │  ← StatusBarView (row Height-1)
└──────────────────────────────────────────────────────┘
```

### `EmptyBuffer`

A simple in-memory buffer used when no file is open. Lives in `CodeEdit.Application` (it has no file I/O and no infrastructure dependency):

```csharp
public sealed class EmptyBuffer : IMutableTextBuffer
{
    private readonly List<string> _lines = [""];
    private CursorPosition _cursor;
    private Selection? _selection;

    public int            LineCount         => _lines.Count;
    public CursorPosition Cursor            => _cursor;
    public Selection?     Selection         => _selection;
    public bool           IsDirty          => false;
    public string?        FilePath         => null;
    public string?        DetectedLanguage => null;

    public string GetLine(int lineIndex) => _lines[lineIndex];

    public void InsertText(CursorPosition at, string text) { /* delegate to same logic as LazyFileBuffer in-memory path */ }
    public void DeleteRange(TextRange range)               { /* same */ }
    public void SetCursor(CursorPosition pos)              { _cursor = pos; }
    public void SetSelection(Selection? selection)         { _selection = selection; }
    public void ResizeCache(int terminalHeight)            { }
}
```

`EmptyBuffer.InsertText` and `DeleteRange` use the same string-splicing logic as `LazyFileBuffer`'s in-memory path. Since `LazyFileBuffer` owns that logic internally, `EmptyBuffer` re-implements it (the logic is simple enough — three lines per method).

### `StatusBarView`

Subscribes to `IEventBus.EventExecuted`, `EventUndone`, `EventRedone` and `ThemeRegistry.ThemeChanged` to refresh. Renders one line at row 0 of its own bounds using the `StatusBar` color pair:

```
 {filePath or "[No File]"}   {dirty? "* " : "  "}  Ln {line+1}, Col {col+1}
```

Left-aligned file path, right-aligned cursor position, dirty marker near the path.

### `AppBootstrap` wiring

```csharp
var menuBar    = new MenuBar(...);  // File menu: Open (no-op), Save (no-op), Quit → app.RequestStop()
var editorView = provider.GetRequiredService<EditorView>();
var statusBar  = provider.GetRequiredService<StatusBarView>();

editorView.X = 0;
editorView.Y = Pos.Bottom(menuBar);
editorView.Width  = Dim.Fill();
editorView.Height = Dim.Fill(1);   // leave 1 row for status bar

statusBar.X = 0;
statusBar.Y = Pos.AnchorEnd(1);
statusBar.Width  = Dim.Fill();
statusBar.Height = 1;

window.Add(menuBar, editorView, statusBar);
```

### Startup file open

```csharp
var args = Environment.GetCommandLineArgs();  // args[0] = exe name
var filePath = args.Length > 1 ? args[1] : null;

IMutableTextBuffer buffer;
if (filePath is not null)
{
    var fileService = provider.GetRequiredService<IFileService>();
    buffer = (IMutableTextBuffer)fileService.Open(filePath);
}
else
{
    buffer = new EmptyBuffer();
}

var eventBus   = provider.GetRequiredService<IEventBus>();
var editorView = provider.GetRequiredService<EditorView>();
eventBus.SetBuffer(buffer);
editorView.SetBuffer(buffer);
```

`IFileService.Open` returns `ITextBuffer`. The cast to `IMutableTextBuffer` is valid because `LazyFileBuffer` implements both. An invalid cast would be a programmer error (no other `ITextBuffer` implementation exists in v1).

### MenuBar (Terminal.Gui v2)

```csharp
var menuBar = new MenuBar
{
    Menus = [
        new MenuBarItem("_File", [
            new MenuItem("_Open", "", null),   // no-op
            new MenuItem("_Save", "", null),   // no-op
            new MenuItem("_Quit", "", () => app.RequestStop()),
        ])
    ]
};
```

## UI / UX

- The editor view takes focus on startup (`editorView.SetFocus()` after `window.Add(...)`)
- `Quit` in the File menu (or pressing Ctrl+Q once keybindings are wired) exits the app
- Status bar shows `[No File]` when no file is open, file path otherwise
- Dirty indicator is `*` after the path when `IsDirty` is true

## Open Questions

None.
