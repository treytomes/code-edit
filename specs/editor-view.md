# Spec: Editor View

## Status
Implemented

## Overview

`EditorView` is the main editing surface. It occupies most of the terminal window, renders buffer lines with a line-number gutter, highlights syntax tokens using the active color theme, draws the cursor, and draws the selection highlight. It subscribes to `IEventBus` events and calls `SetNeedsDisplay()` to trigger redraws. It also routes keyboard input to the event bus as `IBufferEvent` publishes.

For v1 the view renders a plain-text buffer (no file is loaded at startup). When no buffer is set on the bus, the view renders a blank surface.

## Scope

### In scope
- `EditorView` Terminal.Gui `View` subclass
- Line-number gutter (fixed 5-column width: 4 digits + 1 space separator)
- Text area: renders each visible line from the buffer, clipping to view width
- Syntax token coloring via `ISyntaxDetector` / `ISyntaxProvider`
- Cursor rendering: block cursor at `ITextBuffer.Cursor`, drawn via `Driver.SetAttribute` + `Driver.AddStr`
- Selection rendering: highlight all characters within `ITextBuffer.Selection`
- Vertical scrolling: `_scrollRow` tracks the topmost visible line
- Horizontal scrolling: not in scope for v1 (lines clip at view edge)
- Keyboard handling: arrow keys → `SetCursorEvent`; printable chars → `InsertTextEvent`; Backspace → `DeleteEvent`; Enter → `InsertTextEvent("\n")`
- Subscribe to `IEventBus.EventExecuted`, `EventUndone`, `EventRedone` → `SetNeedsDisplay()`
- Subscribe to `ThemeRegistry.ThemeChanged` → `SetNeedsDisplay()`
- Unsubscribe all event handlers in `Dispose`

### Out of scope
- Horizontal scrolling (v1 clips)
- Menu bar, status bar, file-open dialogs (UI layout spec)
- Ctrl+Z / Ctrl+Y keyboard shortcuts (those are menu/keybinding spec concerns — the view routes raw arrow/char/backspace only)
- Syntax provider selection (the view calls `ISyntaxDetector.Detect` once when the buffer is set)
- Word-wrap

## Design

### Constructor signature

```csharp
public sealed class EditorView(
    IEventBus      eventBus,
    ThemeRegistry  themeRegistry,
    ISyntaxDetector syntaxDetector) : View
```

### Fields

```csharp
private readonly IEventBus       _eventBus;
private readonly ThemeRegistry   _themeRegistry;
private readonly ISyntaxDetector _syntaxDetector;
private ISyntaxProvider          _syntaxProvider;   // set when buffer is attached
private int                      _scrollRow;        // index of topmost visible line
private int                      _wantColumn;       // intended column for vertical navigation
```

### Attaching a buffer

`EditorView` does not own the buffer. When `IEventBus.SetBuffer` is called externally (by the open-file flow), the caller also calls `EditorView.SetBuffer(IMutableTextBuffer)` to update the syntax provider and reset scroll state:

```csharp
public void SetBuffer(IMutableTextBuffer buffer)
{
    _syntaxProvider = _syntaxDetector.Detect(buffer.FilePath, buffer.LineCount > 0 ? buffer.GetLine(0) : null);
    _scrollRow = 0;
    SetNeedsDisplay();
}
```

### Draw

`OnDrawContent(Rectangle viewport)` is the Terminal.Gui v2 override (replaces `Redraw`). It runs entirely on the main thread.

```
For each row r in [0, viewport.Height):
    lineIndex = _scrollRow + r
    if lineIndex >= buffer.LineCount:
        clear the row with Normal color and continue

    lineText = buffer.GetLine(lineIndex)
    tokens   = _syntaxProvider.Tokenize([lineText], lineIndex)   // single-line tokenize

    1. Draw gutter (columns 0–4):
         gutterText = (lineIndex + 1).ToString().PadLeft(4) + " "
         SetAttribute(LineNumber)
         Move(r, 0); AddStr(gutterText)

    2. Draw text area (column 5 onward):
         For each character position col in [0, lineText.Length):
             screenCol = 5 + col
             if screenCol >= viewport.Width: break (clip)
             ch       = lineText[col]
             tokenType = TokenTypeAt(tokens, lineIndex, col)
             attr     = ColorFor(tokenType, lineIndex, col, buffer)
             SetAttribute(attr)
             Move(r, screenCol); AddStr(ch.ToString())

         Fill remainder of row to viewport.Width with Normal color spaces

    3. Draw cursor (after text so it overlays):
         if lineIndex == buffer.Cursor.Line:
             cursorScreenCol = 5 + buffer.Cursor.Column
             if cursorScreenCol < viewport.Width:
                 ch = (buffer.Cursor.Column < lineText.Length)
                      ? lineText[buffer.Cursor.Column]
                      : ' '
                 SetAttribute(CursorAttribute)
                 Move(r, cursorScreenCol); AddStr(ch.ToString())
```

**`ColorFor` helper** — returns the Terminal.Gui `Attribute` for a given position, considering selection first:

```
if buffer.Selection != null && PositionInSelection(lineIndex, col, buffer.Selection):
    return ColorPairMapper.ToAttribute(theme.Selection)
return ColorPairMapper.ToAttribute(theme.ForToken(tokenType))
```

**`CursorAttribute`** — use the Selection color pair for the cursor block (high-contrast, visually distinct).

**`TokenTypeAt` helper** — given the token list and a `(line, col)` pair, returns the `TokenType` of the token that spans that column, or `TokenType.Default` if none.

**No-buffer state** — if `_eventBus.Buffer` throws (not yet set), catch `InvalidOperationException`, clear all rows with Normal color, and return.

### Scrolling

After every `EventExecuted` / `EventUndone` / `EventRedone`, `EditorView` calls `ScrollToCursor()` before `SetNeedsDisplay()`:

```csharp
private void ScrollToCursor()
{
    if (!HasBuffer) return;
    var cursorLine = _eventBus.Buffer.Cursor.Line;
    var height     = Bounds.Height;

    if (cursorLine < _scrollRow)
        _scrollRow = cursorLine;
    else if (cursorLine >= _scrollRow + height)
        _scrollRow = cursorLine - height + 1;
}
```

### Keyboard handling

Override `OnKeyDown(Key key)`. For each recognized key, publish to `_eventBus` and return `true` (consumed). Unrecognized keys return `false`.

| Key | Action |
|---|---|
| Arrow Up | move cursor up 1 line, preserving `_wantColumn` |
| Arrow Down | move cursor down 1 line, preserving `_wantColumn` |
| Arrow Left | `SetCursorEvent(MoveLeft, current)` — resets `_wantColumn` |
| Arrow Right | `SetCursorEvent(MoveRight, current)` — resets `_wantColumn` |
| Printable char | `InsertTextEvent(cursor, char.ToString())` — resets `_wantColumn` |
| Enter | `InsertTextEvent(cursor, "\n")` — resets `_wantColumn` |
| Backspace | if cursor not at (0,0): `DeleteEvent(range of char before cursor, text)` — resets `_wantColumn` |
| Delete | if cursor not at end of buffer: `DeleteEvent(range of char at cursor, text)` — resets `_wantColumn` |

**`_wantColumn` — intended column for vertical navigation:**

`_wantColumn` is updated by the view, not stored in the buffer or event. It tracks the column the user "intended" when navigating vertically:

- Any horizontal movement or edit resets `_wantColumn` to the resulting cursor column.
- Vertical movement uses `_wantColumn` as the target column, clamped to the destination line length. `_wantColumn` is NOT updated on vertical movement — it stays until a horizontal action resets it.

Example: cursor at (line=0, col=10) on a 20-char line. Press Down to a 5-char line → cursor lands at (1, 5), but `_wantColumn` stays 10. Press Down again to a 15-char line → cursor lands at (2, 10).

**Clamping helpers:**

```
MoveUp(pos, wantCol, buffer):
    newLine = max(0, pos.Line - 1)
    newCol  = min(wantCol, buffer.GetLine(newLine).Length)
    return new CursorPosition(newLine, newCol)

MoveDown(pos, wantCol, buffer):
    newLine = min(buffer.LineCount - 1, pos.Line + 1)
    newCol  = min(wantCol, buffer.GetLine(newLine).Length)
    return new CursorPosition(newLine, newCol)

MoveLeft(pos, buffer):
    if pos.Column > 0:  new CursorPosition(pos.Line, pos.Column - 1)
    elif pos.Line > 0:  new CursorPosition(pos.Line - 1, buffer.GetLine(pos.Line - 1).Length)
    else: pos  (already at start)

MoveRight(pos, buffer):
    lineLen = buffer.GetLine(pos.Line).Length
    if pos.Column < lineLen: new CursorPosition(pos.Line, pos.Column + 1)
    elif pos.Line < buffer.LineCount - 1: new CursorPosition(pos.Line + 1, 0)
    else: pos  (already at end)
```

**Backspace delete range:**

```
if pos.Column > 0:
    range = TextRange(new CursorPosition(pos.Line, pos.Column - 1), pos)
    text  = buffer.GetLine(pos.Line)[pos.Column - 1].ToString()
else:  // pos.Line > 0 (already guarded)
    prevLine = buffer.GetLine(pos.Line - 1)
    range = TextRange(new CursorPosition(pos.Line - 1, prevLine.Length), pos)
    text  = "\n"
```

**Delete key range:**

```
lineLen = buffer.GetLine(pos.Line).Length
if pos.Column < lineLen:
    range = TextRange(pos, new CursorPosition(pos.Line, pos.Column + 1))
    text  = buffer.GetLine(pos.Line)[pos.Column].ToString()
else if pos.Line < buffer.LineCount - 1:
    range = TextRange(pos, new CursorPosition(pos.Line + 1, 0))
    text  = "\n"
```

### Event subscriptions

In the constructor (after field init):

```csharp
_eventBus.EventExecuted += OnBufferChanged;
_eventBus.EventUndone   += OnBufferChanged;
_eventBus.EventRedone   += OnBufferChanged;
_themeRegistry.ThemeChanged += OnThemeChanged;
```

Handlers:

```csharp
private void OnBufferChanged(object? sender, BufferEventArgs e)
{
    ScrollToCursor();
    SetNeedsDisplay();
}

private void OnThemeChanged(object? sender, EventArgs e) => SetNeedsDisplay();
```

`Dispose` override:

```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _eventBus.EventExecuted -= OnBufferChanged;
        _eventBus.EventUndone   -= OnBufferChanged;
        _eventBus.EventRedone   -= OnBufferChanged;
        _themeRegistry.ThemeChanged -= OnThemeChanged;
    }
    base.Dispose(disposing);
}
```

### Terminal.Gui v2 draw API

In Terminal.Gui v2, drawing inside `OnDrawContent` uses:

- `Application.Driver.SetAttribute(attr)` — sets current color
- `Move(row, col)` — positions the virtual cursor (inherited `View` method, col then row)
- `Application.Driver.AddStr(str)` — writes a string at the current position

`Bounds` gives the view's local rectangle (Width × Height). The `viewport` parameter passed to `OnDrawContent` is the clipping region.

## UI / UX

```
┌─────────────────────────────────────────────┐
│   1 fn main() {                             │
│   2     println!("hello");                  │
│   3 }                                       │
│   4                                         │  ← cursor on line 4, col 0 (blank line)
│   5                                         │
└─────────────────────────────────────────────┘
  ^^^^ gutter (LineNumber color)
       ^^^^^^^^^^^^^^^^^^^^^^^^^^^^ text area
```

- Gutter is 5 columns wide (4-digit line number right-aligned + 1 space)
- Selected text highlighted with Selection color
- Cursor drawn as a block overlay using Selection color pair

## Open Questions

None.
