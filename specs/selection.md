# Spec: Text Selection and Edit Menu

## Status
Implemented

## Overview
Add text selection to the editor — via mouse click-and-drag and keyboard shortcuts — and expose cut/copy/paste through an Edit menu in addition to the existing keyboard shortcuts. Selection is visually highlighted using the theme's Selection color. Copy/cut/paste already work; this spec wires selection as the prerequisite input.

## Scope

**In scope**
- Mouse click to place cursor (no selection)
- Mouse click-and-drag to select a range
- Shift+Arrow keys (character-wise selection)
- Shift+Ctrl+Left / Shift+Ctrl+Right (word-boundary selection)
- Shift+Home / Shift+End (line-boundary selection)
- Ctrl+A (select all)
- Edit menu: Cut, Copy, Paste items wired to existing clipboard commands
- Selection cleared by any non-shift keystroke that moves the cursor or inserts/deletes text
- Typing with active selection replaces the selected text
- Backspace/Delete with active selection deletes the selected text (not the adjacent character)
- Enter with active selection replaces selected text with a newline
- Arrow Left/Up with selection collapses to the normalized start of the selection
- Arrow Right/Down with selection collapses to the normalized end of the selection
- `_wantColumn` updated to the collapsed column on selection-collapse so subsequent vertical nav is consistent

**Out of scope**
- Double-click word selection
- Triple-click line selection
- Rectangular / column selection
- Selection via mouse wheel
- Keyboard-only selection persistence across undo (selection is not undoable)

## Design

### Domain — `Selection` type (already exists)
`Selection` holds `Anchor` and `Active` `CursorPosition` values. `Anchor` is fixed when selection starts; `Active` tracks the moving end. The buffer's `Selection` property is already `Selection?`.

### SetSelectionEvent (new `IBufferEvent`)
Mutates `buffer.Selection` and `buffer.Cursor` atomically.

```
SetSelectionEvent(Selection? newSelection, CursorPosition newCursor,
                  Selection? previousSelection, CursorPosition previousCursor)
```

Undo restores `previousSelection` and `previousCursor`. Cursor-move events that extend or clear a selection replace `SetCursorEvent` — they use `SetSelectionEvent` instead.

**No coalescing** for selection events — each shift+arrow step is a separate undo unit (VS Code behaviour).

### Clearing the selection
Any cursor move without Shift, and any insert/delete, clears the selection. `InsertTextEvent` and `DeleteEvent` already call `buffer.SetSelection(null)` — verify and add if missing.

### Selection-aware editing

When `buffer.Selection.HasValue`, the editing key handlers (type, Backspace, Delete, Enter) must first replace the selection before applying the normal operation:

- **Type / Enter**: Delete the selected range, then insert the typed character/newline at the selection start. Implemented by publishing a single `InsertTextEvent` with `replaceRange = selection`. The event captures the replaced text for undo.
- **Backspace / Delete**: Delete the selected range and do nothing else. Publish `DeleteEvent(selectionRange, selectedText)`.

Undo restores the replaced text and selection.

### Selection-collapse on arrow keys

When `buffer.Selection.HasValue` and the user presses a non-Shift arrow key:
- Left / Up → move cursor to `Normalise(selection).start`, clear selection, consume the key (do not move an additional step).
- Right / Down → move cursor to `Normalise(selection).end`, clear selection, consume the key.

`_wantColumn` is set to the collapsed column. Publish `SetSelectionEvent(null, collapsedPos, ...)` to record the cursor move.

### Word boundary helper
`WordBoundaryLeft(pos, buffer)` / `WordBoundaryRight(pos, buffer)` — scan left/right from cursor, stop at whitespace→non-whitespace or non-whitespace→whitespace transition. Static methods on `EditorView` (presentation concern, like existing `BackspaceRange`).

### Mouse selection

`EditorView.OnMouseEvent(Mouse mouse)`:
1. **Click (LeftButtonPressed, no drag yet)**: convert `mouse.Position` (viewport-relative) to buffer `(line, col)` via `ScreenToBuffer`, call `Publish(new SetSelectionEvent(null, newPos, ...))`, grab mouse via `_app.Mouse.GrabMouse(this)`, set `_dragAnchor = newPos`.
2. **Drag (PositionReport while grabbed)**: convert position to buffer coords, call `Publish(new SetSelectionEvent(new Selection(_dragAnchor, activePos), activePos, ...))`.
3. **Release (LeftButtonReleased)**: `_app.Mouse.UngrabMouse()`, clear `_dragAnchor`.

`EditorView` needs `IApplication _app` injected (already available via DI after `app.Init()`).

`ScreenToBuffer(Point screenPos)`:
- `line = Clamp(_scrollRow + screenPos.Y, 0, buffer.LineCount - 1)`
- `col  = Clamp(screenPos.X - GutterWidth, 0, buffer.GetLine(line).Length)`

### Keyboard selection shortcuts

All handled in `OnKeyDown` before the existing cursor-move branches. When a Shift modifier is present:
- Shift+Up/Down/Left/Right: extend or start selection — `Anchor` = existing `Anchor ?? currentCursor`, `Active` = new cursor position.
- Shift+Ctrl+Left/Right: same but jump to word boundary.
- Shift+Home: Active → column 0.
- Shift+End: Active → end of line.
- Ctrl+A: Select all — `Anchor = (0,0)`, `Active = (lastLine, lastLineLen)`.

When no Shift modifier and the event moves the cursor (arrows, Home, End), clear the selection. This is already the path taken by `SetCursorEvent`; we need the handler to also call `buffer.SetSelection(null)` — easiest by making non-shift cursor moves publish `SetSelectionEvent(null, newPos, ...)` instead of `SetCursorEvent`.

Alternatively: keep `SetCursorEvent` but add `SetCursorEvent.Execute` clearing selection. **Chosen approach**: extend `SetCursorEvent.Execute` to always call `buffer.SetSelection(null)`. Shift-moves publish `SetSelectionEvent` instead.

### Edit menu

```
_Edit
  Cut    Ctrl+X
  Copy   Ctrl+C
  Paste  Ctrl+V
```

Actions call the same code paths as keyboard shortcuts. Status bar messages ("No text selected", "Clipboard not available") are shown the same way.

## UI / UX

### Mouse cursor placement
Clicking in the gutter column (col < GutterWidth) snaps cursor to column 0 of that line.

### Selection highlight
Already implemented in `EditorView.OnDrawingContent` — `PositionInSelection` check applies `selectionAttr`. This works as-is once `buffer.Selection` is set.

### Visual feedback
No caret blink. Selection highlight is the sole visual indicator.

### Edit menu layout

```
┌─────────────────────────────────────┐
│ _File  _Edit                        │
├─────────────────────────────────────┤
│       ┌──────────────────┐          │
│       │ Cu_t    Ctrl+X   │          │
│       │ _Copy   Ctrl+C   │          │
│       │ _Paste  Ctrl+V   │          │
│       └──────────────────┘          │
```

## Open Questions
_(none — resolved below)_

- **Undo granularity for selection**: Selection changes are not pushed onto the undo stack as standalone events. They are part of the cursor-move events. Undo of a cut restores the text and the selection that existed before the cut (captured in `CutEvent`).
- **Shift+click**: Not in scope for v1.
