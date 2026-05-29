# Spec: Mouse Wheel Scrolling

## Status
Implemented

## Overview
Allow the user to scroll the editor viewport using the mouse wheel. Scrolling moves the viewport without changing the cursor position; the cursor remains where it is and becomes off-screen if scrolled past.

## Scope

**In scope**
- `WheeledUp` scrolls viewport up (toward start of file) by 3 lines
- `WheeledDown` scrolls viewport down (toward end of file) by 3 lines
- Clamped at buffer boundaries (cannot scroll past start or end)
- Works in both wrap-off (logical lines) and wrap-on (visual rows) modes

**Out of scope**
- Horizontal scroll (no horizontal scrolling in v1)
- Scroll bar widget
- Trackpad inertia / acceleration

## Design

### `OnMouseEvent` additions
In `EditorView.OnMouseEvent`, handle `WheeledUp` and `WheeledDown` flags before the drag logic:

```csharp
const int ScrollLines = 3;

if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
{
    ScrollBy(-ScrollLines);
    mouse.Handled = true;
    return true;
}

if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
{
    ScrollBy(ScrollLines);
    mouse.Handled = true;
    return true;
}
```

### `ScrollBy(int delta)`
- Wrap off: `_scrollRow = Clamp(_scrollRow + delta, 0, buffer.LineCount - 1)`
- Wrap on: `_scrollVisualRow = Clamp(_scrollVisualRow + delta, 0, wrapLayout.Count - 1)`
- Call `SetNeedsDraw()`.

Cursor does not move. `ScrollToCursor` is NOT called on wheel scroll (the whole point is to decouple scroll from cursor).

### Interaction with `ScrollToCursor`
`ScrollToCursor` is NOT called on wheel events — the whole point is to decouple the viewport from the cursor position temporarily.

`ScrollToCursor` IS called on every `EventExecuted`/`EventUndone`/`EventRedone`, which includes cursor-move events (arrow keys, mouse click, selection collapse, etc.) as well as text edits. So any deliberate cursor movement snaps the viewport back to show the cursor, even if the user had previously wheeled away.

## UI / UX

No visual chrome (no scrollbar). The viewport simply moves. This is consistent with the current keyboard-scroll-on-cursor-move behaviour — the gutter line numbers shift to reflect the new viewport position.

## Open Questions
_(none)_
