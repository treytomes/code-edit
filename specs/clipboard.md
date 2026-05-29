# Spec: Clipboard

## Status
Implemented

## Overview

Adds OS clipboard integration to the editor: Copy (Ctrl+C), Cut (Ctrl+X), and Paste (Ctrl+V). Copy and Cut read the current selection (or the whole current line when nothing is selected); Paste inserts the clipboard text at the cursor, replacing any active selection first. All three operations flow through the event bus as `IBufferEvent` implementations.

## Scope

### In scope
- `CopyCommand` — reads selection (or current line) → writes to OS clipboard; no buffer mutation, no undo entry
- `CutEvent` — copies to clipboard then deletes the selected range (or current line); undoable
- `PasteEvent` — optionally deletes selection, then inserts clipboard text at cursor; undoable
- Keyboard bindings in `EditorView`: Ctrl+C, Ctrl+X, Ctrl+V
- OS clipboard access via `IApplication.Clipboard` (`TryGetClipboardData` / `TrySetClipboardData`)
- `IClipboardService` port in `CodeEdit.Application.Ports` — abstracts clipboard so it is testable without Terminal.Gui
- `TGuiClipboardService` in `CodeEdit.Presentation` — wraps `IApplication.Clipboard`

### Out of scope
- Internal (non-OS) cut buffer
- Menu wiring for Edit > Cut/Copy/Paste (edit-menu spec)
- Shift+arrow selection (selection spec)

## Design

### `IClipboardService` (Application/Ports)

```csharp
public interface IClipboardService
{
    bool TryGet(out string text);
    bool TrySet(string text);
}
```

### `TGuiClipboardService` (Presentation)

```csharp
public sealed class TGuiClipboardService(IApplication app) : IClipboardService
{
    public bool TryGet(out string text) => app.Clipboard.TryGetClipboardData(out text);
    public bool TrySet(string text)    => app.Clipboard.TrySetClipboardData(text);
}
```

Registered as `IClipboardService` singleton in `AppBootstrap`. `IApplication` is available as `app` at bootstrap time — pass it directly to the constructor.

### `CopyCommand` (Application/Events)

Copy is not a buffer mutation — it has no `Execute`/`Undo` semantics and is NOT an `IBufferEvent`. It is a plain method called directly from `EditorView`:

```csharp
public static class CopyCommand
{
    public static bool Execute(ITextBuffer buffer, IClipboardService clipboard)
    {
        if (!buffer.Selection.HasValue) return false;  // no selection → nothing to copy
        var text = SelectedText(buffer);
        return clipboard.TrySet(text);
    }

    internal static string SelectedText(ITextBuffer buffer) { ... }
}
```

**Ctrl+C with no selection does nothing** (returns false; caller shows "No text selected" in status bar). This matches VS Code where Ctrl+C with no selection copies the line — but the user explicitly chose not to do that here.

**Selection text extraction:** Normalises `(Anchor, Active)` to `(start, end)`. Single-line selection: substring. Multi-line: first-line suffix + `"\n"` + middle lines + `"\n"` + last-line prefix.

### `CutEvent` (Application/Events)

```csharp
public sealed class CutEvent(ITextBuffer buffer, IClipboardService clipboard) : IBufferEvent
{
    private readonly TextRange _range    = SelectionRange(buffer);
    private readonly string    _text     = buffer.Selection.HasValue
                                           ? SelectedText(buffer)
                                           : CurrentLine(buffer);
    private readonly bool      _fullLine = !buffer.Selection.HasValue;

    public void Execute(IMutableTextBuffer buf)
    {
        clipboard.TrySet(_text);
        buf.DeleteRange(_range);
        buf.SetCursor(_range.Start);
        buf.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buf)
    {
        buf.InsertText(_range.Start, _text);
        buf.SetCursor(_range.End);
        buf.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }
}
```

**`SelectionRange`** — if `buffer.Selection` is non-null, returns `TextRange(Anchor, Active)` normalised. If null (no selection), returns the range covering the entire current line including its trailing newline:
- `start = new CursorPosition(line, 0)`
- `end   = line + 1 < buffer.LineCount ? new CursorPosition(line + 1, 0) : new CursorPosition(line, buffer.GetLine(line).Length)`

`CutEvent` captures `_range` and `_text` at construction time (before Execute is called), so undo has what it needs even if the buffer has changed by then.

### `PasteEvent` (Application/Events)

```csharp
public sealed class PasteEvent(ITextBuffer buffer, IClipboardService clipboard) : IBufferEvent
{
    private readonly string        _pasteText     = GetClipboardText(clipboard);
    private readonly CursorPosition _insertAt      = InsertPoint(buffer);
    private readonly TextRange?    _deletedRange  = buffer.Selection.HasValue
                                                     ? NormaliseRange(buffer.Selection.Value) : null;
    private readonly string?       _deletedText   = buffer.Selection.HasValue
                                                     ? SelectedText(buffer) : null;

    public void Execute(IMutableTextBuffer buf)
    {
        if (_deletedRange.HasValue)
        {
            buf.DeleteRange(_deletedRange.Value);
            buf.SetSelection(null);
        }
        buf.InsertText(_insertAt, _pasteText);
        var endPos = EndPosition(_insertAt, _pasteText);
        buf.SetCursor(endPos);
        buf.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buf)
    {
        // Remove pasted text
        var pasteEnd = EndPosition(_insertAt, _pasteText);
        buf.DeleteRange(new TextRange(_insertAt, pasteEnd));

        // Restore deleted selection if there was one
        if (_deletedRange.HasValue && _deletedText is not null)
        {
            buf.InsertText(_deletedRange.Value.Start, _deletedText);
            buf.SetCursor(_deletedRange.Value.End);
        }
        else
        {
            buf.SetCursor(_insertAt);
        }
        buf.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }
}
```

**`InsertPoint`** — if selection is non-null, the normalised start of the selection (text is inserted there after the selection is deleted). Otherwise `buffer.Cursor`.

**`GetClipboardText`** — `clipboard.TryGet(out var t) ? t : string.Empty`.

**`EndPosition`** — same helper as in `InsertTextEvent`.

All text/range values are captured at construction time (snapshot of buffer state), not at Execute time.

### `IClipboardService` status feedback

`IClipboardService` gains a `bool IsSupported` property. `EditorView` checks this when a clipboard operation is requested; if false it sets a status message on `StatusBarView` rather than silently failing.

`StatusBarView` gains a `void SetMessage(string? message)` method. When a message is set it overrides the normal file/cursor display until the next buffer-changed event, at which point it clears back to normal.

### Keyboard handling in `EditorView`

`key.KeyCode` carries the modifier mask — use `KeyCode.CtrlMask | KeyCode.C` (not `key.IsCtrl && key.KeyCode == KeyCode.C`).

Add to `OnKeyDown`, before the printable-rune check:

```csharp
if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.C))
{
    if (!_clipboardService.IsSupported)
        _statusBar.SetMessage("Clipboard not available");
    else if (!CopyCommand.Execute(_eventBus.Buffer, _clipboardService))
        _statusBar.SetMessage("No text selected");
    key.Handled = true;
    return true;
}

if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.X))
{
    if (!_clipboardService.IsSupported)
        _statusBar.SetMessage("Clipboard not available");
    else
        _eventBus.Publish(new CutEvent(_eventBus.Buffer, _clipboardService));
    key.Handled = true;
    return true;
}

if (key.KeyCode == (KeyCode.CtrlMask | KeyCode.V))
{
    if (!_clipboardService.IsSupported)
        _statusBar.SetMessage("Clipboard not available");
    else
        _eventBus.Publish(new PasteEvent(_eventBus.Buffer, _clipboardService));
    key.Handled = true;
    return true;
}
```

`EditorView` gains `IClipboardService _clipboardService` as a constructor parameter.

### `AppBootstrap` DI update

```csharp
services.AddSingleton<IClipboardService>(new TGuiClipboardService(app));
```

`app` is the `IApplication` instance created just before `app.Init()`. `TGuiClipboardService` is registered after `app.Init()` has been called (clipboard is available after init).

## UI / UX

- Ctrl+C with selection → copies selection; no visual change
- Ctrl+C with no selection → status bar shows "No text selected"; clipboard unchanged
- Ctrl+X with selection → copies and removes selection; cursor at selection start
- Ctrl+X with no selection → copies and removes current line (VS Code behaviour); cursor at line start
- Ctrl+V → pastes at cursor (replacing selection if active); cursor at end of pasted text
- If clipboard is unavailable (e.g. no `xclip` on Linux) → status bar shows "Clipboard not available"
- Status bar message clears on the next buffer change (keypress, undo, redo)

## Test Cases

All tests in `tests/CodeEdit.Tests/Application/ClipboardCommandTests.cs`.

### CopyCommand
1. Copy with selection → clipboard contains selected text, returns true
2. Copy with no selection → returns false, clipboard unchanged
3. Copy does not modify buffer

### CutEvent
4. Cut with selection → clipboard has selection text, selection removed from buffer
5. Cut with no selection → clipboard has full line + `"\n"`, line removed from buffer
6. Cut.Undo → removed text restored, cursor at end of restored text

### PasteEvent
7. Paste with no selection → text inserted at cursor, cursor at end of pasted text
8. Paste with selection → selection deleted, clipboard text inserted at selection start
9. Paste.Undo (no prior selection) → pasted text removed, cursor at original position
10. Paste.Undo (with prior selection) → pasted text removed, selection text restored

## Open Questions

None.
