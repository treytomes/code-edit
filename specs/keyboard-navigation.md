# Spec: Keyboard Navigation

## Status
Implemented

## Overview
Fill in the remaining keyboard navigation gaps: Tab/Shift+Tab for indentation,
Ctrl+Left/Right for word-boundary cursor movement, and Ctrl+Up/Down for viewport
scrolling. Also introduces a user settings file that controls indentation style
(spaces vs. tab character) and tab width, and ensures `\t` characters are
rendered at the correct visual width in the editor viewport.

Ctrl+Shift+Left/Right (word-boundary selection) is already implemented and is
documented here for completeness.

## Scope

**In scope**
- `Tab` — insert indent at cursor (no selection), or indent each touched line (selection)
- `Shift+Tab` — dedent current line (no selection), or dedent each touched line (selection)
- `Ctrl+Left` — move cursor to previous word start; collapse selection if active
- `Ctrl+Right` — move cursor to next word start; collapse selection if active
- `Ctrl+Up` — scroll viewport up 1 line without moving cursor
- `Ctrl+Down` — scroll viewport down 1 line without moving cursor
- `Ctrl+Shift+Left` — extend/shrink selection to previous word start (already implemented)
- `Ctrl+Shift+Right` — extend/shrink selection to next word start (already implemented)
- `EditorSettings` — settings file at `~/.code-edit/settings.json` controlling
  indentation style and tab width
- `\t` visual rendering — expand tab characters to the correct visual width in
  `DrawSegment` based on `EditorSettings.TabWidth`

**Out of scope**
- Per-language or per-project settings overrides (v2+)
- Tab stops aligned to column multiples (indent is always exactly `TabWidth` spaces or one `\t`)
- Auto-detection of existing file indentation style
- Settings hot-reload while the editor is running — **follow up in v2**

## Design

### User settings

A JSON file at `~/.code-edit/settings.json` is loaded once at startup. Missing
file or missing/invalid keys fall back to defaults. Unknown keys are ignored.

```jsonc
{
  "tabWidth": 4,         // columns per tab stop (default: 4)
  "insertSpaces": true,  // true = insert spaces on Tab; false = insert \t (default: true)
  "recentFilesMax": 10   // maximum number of recent files to remember (default: 10)
}
```

**`EditorSettings` record** (in `CodeEdit.Domain`):

```csharp
public sealed record EditorSettings(int TabWidth, bool InsertSpaces, int RecentFilesMax)
{
    public static readonly EditorSettings Default = new(TabWidth: 4, InsertSpaces: true, RecentFilesMax: 10);
    public string IndentString => InsertSpaces ? new string(' ', TabWidth) : "\t";
}
```

**`SettingsService`** (in `CodeEdit.Infrastructure.Settings`):

```csharp
public sealed class SettingsService(ILogger<SettingsService> logger)
{
    public EditorSettings Load(); // reads ~/.code-edit/settings.json; returns Default on any error
}
```

`SettingsService` is registered as a singleton. `EditorSettings` is resolved once
at startup via `SettingsService.Load()` and injected into `EditorView`. There is
no hot-reload in v1 — **follow up in v2**.

### \t visual rendering

`EditorView.DrawSegment` currently iterates characters and writes them directly.
`\t` passed through `AddStr` is rendered by the terminal emulator at 8-column
stops, which is wrong.

Fix: expand `\t` to spaces in `DrawSegment`. For each character position, if the
character is `\t`, substitute `N` spaces where `N` is the number of columns to
the next tab stop:

```
N = TabWidth - (currentVisualColumn % TabWidth)
```

`currentVisualColumn` is tracked as a local variable within the draw loop,
accumulating the visual width of each character (1 for normal chars, N for `\t`).

This expansion affects:
- Correct visual column counting in `DrawSegment`
- Correct selection highlighting (the expanded spaces are highlighted if the `\t`
  is within the selection)
- `_wantColumn` persistence: the `VisualColOf` helper must also expand `\t` using
  the same formula when computing the visual column of a logical position

All other logical operations (cursor column, insert/delete positions) remain in
logical (byte) columns. The visual ↔ logical translation lives only in drawing
and `VisualColOf`.

### IndentEvent

New event in `CodeEdit.Application.Events`:

```csharp
public sealed class IndentEvent : IBufferEvent
{
    // lines: sorted list of logical line indices to indent/dedent
    // indentString: the string to prepend (e.g. "    " or "\t")
    // dedent: true = remove leading indent, false = add
    // removedLeading[i]: length of leading text removed per line (captured at Execute for Undo)
}
```

**Execute (indent):** For each line index, call
`buffer.InsertText(new CursorPosition(line, 0), indentString)`.

**Execute (dedent):** For each line index, count how many leading characters
match the indent string (up to `indentString.Length`). If `insertSpaces`, count
leading spaces up to `TabWidth`. If tab character, check if line[0] == `\t`.
Store the count in `removedLeading`, then
`buffer.DeleteRange(range covering those characters)`.

**Undo (indent):** For each line, delete the prepended characters from column 0.

**Undo (dedent):** For each line, re-insert the originally removed string at
column 0.

**TryCoalesce:** Never coalesce.

After publishing `IndentEvent`, `EditorView` adjusts the cursor/selection
endpoints: for each endpoint on a line that was modified, shift its column by
`+indentString.Length` (indent) or `-removedLeading[lineIndex]` (dedent),
clamped to `[0, lineLength]`. This is done in `EditorView` after publishing, not
inside the event itself, to keep event logic free of selection concerns.

### Tab / Shift+Tab — no selection

**Tab:** Insert `indentString` at the cursor via `InsertTextEvent`. This moves the cursor forward by `indentString.Length`, matching typing behaviour.

**Shift+Tab:** Dedent the current line. Publish `IndentEvent([cursorLine], indentString, dedent: true)`. If leading indent is 0, do nothing.

### Tab / Shift+Tab — single-line selection

Both endpoints on the same line: indent/dedent the whole line via `IndentEvent`, same as multi-line. This matches VS Code.

### Tab / Shift+Tab — multi-line selection

A line is "touched" if any character on it is within the selection, with one
exception: if the active (non-anchor) endpoint of the selection is at column 0,
that line is excluded (VS Code behaviour).

**Tab:** Collect touched line indices. Publish `IndentEvent(lines, indentString, dedent: false)`. Adjust selection.

**Shift+Tab:** Same line set. Publish `IndentEvent(lines, indentString, dedent: true)`. Adjust selection.

### Ctrl+Left / Ctrl+Right

Reuses existing `WordBoundaryLeft` / `WordBoundaryRight` helpers.

- **With active selection:** ignore the selection word-move; collapse to the
  appropriate selection end first (start for Left, end for Right), then perform
  the word move from that position. Matches VS Code.
- **Without selection:** move cursor directly, clear selection.

Publishes `SetSelectionEvent(null, newPos, prevSelection, prevCursor)`.

### Ctrl+Up / Ctrl+Down

Scroll the viewport by 1 line without moving cursor or selection.

- **Wrap off:** `_scrollRow ±= 1`, clamped to `[0, max(0, LineCount - 1)]`
- **Wrap on:** `_scrollVisualRow ±= 1`, clamped to `[0, max(0, wrapLayout.Count - 1)]`

Does not call `ScrollToCursor`. Calls `SetNeedsDraw()` directly. Does not publish
any event (no undo needed for a viewport scroll).

## File layout

New and changed files:

```
src/CodeEdit.Domain/
  EditorSettings.cs               — NEW

src/CodeEdit.Infrastructure/
  Settings/
    SettingsService.cs            — NEW

src/CodeEdit.Application/
  Events/
    IndentEvent.cs                — NEW

src/CodeEdit.Presentation/
  AppBootstrap.cs                 — CHANGED: register SettingsService, load settings,
                                    inject EditorSettings into EditorView
  Views/EditorView.cs             — CHANGED: Tab/Shift+Tab, Ctrl+arrows, Ctrl+Up/Down,
                                    \t expansion in DrawSegment and VisualColOf
```

## UI / UX

No visible UI changes beyond correct tab rendering. Behaviour matches VS Code:

| Key | Action |
|---|---|
| `Tab` (no selection or single-line) | Indent current line |
| `Tab` (multi-line selection) | Indent all touched lines |
| `Shift+Tab` | Dedent current line or all touched lines |
| `Ctrl+Left` | Move cursor to previous word start |
| `Ctrl+Right` | Move cursor to next word start |
| `Ctrl+Up` | Scroll viewport up 1 line |
| `Ctrl+Down` | Scroll viewport down 1 line |
| `Ctrl+Shift+Left` | Extend selection to previous word start |
| `Ctrl+Shift+Right` | Extend selection to next word start |

## Open Questions

None.