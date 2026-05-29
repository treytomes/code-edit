# Spec: Word Wrap

## Status
Implemented

## Overview
Add a toggleable soft word-wrap mode to the editor. When enabled, logical lines longer than the viewport width are displayed across multiple visual rows, reflowing automatically on terminal resize. Line numbering shows the logical line number only on the first visual row of each line; continuation rows have a blank gutter.

## Scope

**In scope**
- Soft wrap at viewport edge (reflows on resize)
- Alt+Z keyboard shortcut to toggle (VS Code default)
- View menu with a "Word Wrap" toggle item (checked state reflects current mode)
- Blank gutter on continuation rows
- Cursor navigation aware of visual rows (Up/Down move by visual row when wrap is on)
- Mouse click maps screen position to correct logical (line, col)
- Scroll position maintained in logical lines; visual-row scroll derived from wrap layout

**Out of scope**
- Fixed-column wrap
- Per-file wrap settings
- Horizontal scrolling when wrap is off (future)
- Word-break vs character-break control (always break at character boundary for v1)

## Design

### Wrap state
A single boolean `_wordWrap` field on `EditorView`. Default: `false` (off). Toggled by the Alt+Z handler and the View menu action — both call the same `ToggleWordWrap()` method.

### Visual layout: `WrapLayout`
When word wrap is on, rendering and navigation require a mapping from logical lines to visual rows. Compute this lazily on each draw (the viewport width may change between draws).

```
record VisualRow(int LogicalLine, int StartCol, int EndCol);
```

`BuildWrapLayout(ITextBuffer buffer, int viewportWidth)` returns `List<VisualRow>`:
- For each logical line, slice into segments of `viewportWidth - GutterWidth` characters.
- A line with zero characters produces one `VisualRow` with `StartCol = EndCol = 0`.

This list is rebuilt at draw time and cached (invalidated when buffer changes or viewport resizes). It is only used within a single draw + input cycle, so it does not need to be stored on the heap between frames — it can be a local in `OnDrawingContent` that is also used during the same frame's input handling by storing it as a field until the next draw.

### Drawing with wrap on
Replace the line-index loop with a visual-row loop:

```
for (var vr = 0; vr < wrapLayout.Count; vr++)
{
    var screenRow = vr - _scrollVisualRow;
    if (screenRow < 0) continue;
    if (screenRow >= height) break;

    var row = wrapLayout[vr];
    var isFirstRow = row.StartCol == 0;
    // Gutter: show line number for first row, blank for continuations
    // Text: buffer.GetLine(row.LogicalLine)[row.StartCol..row.EndCol]
    // Selection + cursor: map buffer position to visual position
}
```

`_scrollVisualRow` replaces `_scrollRow` as the scroll state when wrap is on. When wrap is off, continue using `_scrollRow` (logical line index).

### Cursor navigation with wrap on

Up/Down move by one visual row (not one logical line):
- Find the current visual row index from the cursor.
- Up → previous visual row; Down → next visual row.
- `_wantColumn` is in screen-column space relative to the viewport (i.e. `cursor.Column - row.StartCol`). Clamp to the target row's character count.

Left/Right continue to move by one character in logical space; they naturally cross visual row boundaries.

### `ScreenToBuffer` with wrap on
Mouse click: given `(screenX, screenY)`:
- `visualRowIndex = _scrollVisualRow + screenY`
- Look up `wrapLayout[visualRowIndex]` → `VisualRow`
- `col = Clamp(row.StartCol + (screenX - GutterWidth), 0, row.EndCol)`

### Scroll
- `ScrollToCursor` is called on every `EventExecuted`/`EventUndone`/`EventRedone`, so any cursor move (arrow keys, mouse click, text edit, undo) snaps the viewport to show the cursor — even if the user had previously wheeled away.
- When wrap is on, `ScrollToCursor` operates on `_scrollVisualRow`: it finds the visual row index of the cursor and ensures it falls within `[_scrollVisualRow, _scrollVisualRow + height)`.
- When wrap is off, `_scrollRow` is used as before (logical lines).

### Resize handling
Override `OnLayoutComplete()` (or equivalent Terminal.Gui v2 hook) to invalidate the wrap layout cache and call `SetNeedsDraw()`. If no explicit resize event is available, rebuild the layout on every draw (it's cheap for files of reasonable size).

## UI / UX

### Gutter appearance

Wrap off (current behaviour):
```
   1 hello world this is a very long line that extends past the viewport
   2 short line
```

Wrap on:
```
   1 hello world this is a very long line that extends
     past the viewport
   2 short line
```
Continuation rows show four spaces + one space (same gutter width, blank).

### View menu

```
_View
  [✓] _Word Wrap    Alt+Z
```

The check mark reflects `_wordWrap`. Toggle action calls `ToggleWordWrap()`.

### Status bar
No change — cursor position continues to show logical (line, col).

## Open Questions
_(none)_
