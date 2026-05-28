# Spec: Search and Replace

## Status
Approved

## Overview
A Find/Replace bar that slides in at the bottom of the editor viewport (above the
status bar). Find highlights all matches in the visible area and selects the
current match; Replace substitutes one or all matches. Behaviour follows VS Code
and DOS EDIT conventions where they align; VS Code wins on ambiguities.

## Scope

**In scope**
- Find bar (Ctrl+F): text input, match count, prev/next navigation
- Find & Replace bar (Ctrl+H): extends Find bar with a replace input + buttons
- Case-sensitive toggle (default: off)
- Whole-word toggle (default: off)
- Find / Find Next (Ctrl+F / F3) while bar is open or closed
  (Ctrl+F opens or focuses the bar; F3 advances to next match, reopening the bar
  if closed; repeats last search after bar is dismissed)
- Find Previous (Shift+F3) while bar is open or closed
- Replace current match (Enter or Replace button while Replace bar is open)
- Replace All button
- Dismiss bar with Escape; editor regains focus
- Match highlights: all visible matches dimmed; current match shown in selection
  colour
- Match counter label: "3 of 14 matches" (or "No matches")
- Wrap-around: search wraps from end-of-file back to start (and vice versa),
  with a brief "Wrapped" indicator in the match counter

**Out of scope**
- Regular-expression search (v2+)
- Incremental/live-filtering of the file tree (v2+)
- Search across multiple files
- Search history / dropdown

## Design

### SearchService (Application layer)

`SearchService` operates on an `ITextBuffer` and is stateless between calls
(the caller holds the current match position).

```csharp
public sealed class SearchService
{
    // Returns all match positions in the buffer for the given query.
    public IReadOnlyList<CursorPosition> FindAll(
        ITextBuffer buffer, string query, bool matchCase, bool wholeWord);

    // Returns the next match position after `from`, wrapping around.
    // Returns null if there are no matches.
    // `wrapped` is set to true when the result wraps past end-of-file.
    public CursorPosition? FindNext(
        ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
        CursorPosition from, out bool wrapped);

    // Returns the previous match position before `from`, wrapping around.
    public CursorPosition? FindPrev(
        ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
        CursorPosition from, out bool wrapped);

    // Replaces the text at `match` with `replacement`.
    // Returns the buffer event to publish (an InsertTextEvent replacing the range).
    public (CursorPosition matchEnd, IBufferEvent ev) BuildReplaceEvent(
        ITextBuffer buffer, CursorPosition match,
        string query, string replacement, bool matchCase, bool wholeWord);

    // Replaces all matches. Returns the number of replacements made.
    // Caller should publish the returned events in order.
    public (int count, IReadOnlyList<IBufferEvent> events) BuildReplaceAllEvents(
        ITextBuffer buffer, string query, string replacement,
        bool matchCase, bool wholeWord);
}
```

**Match position**: a `CursorPosition` pointing to the start column of the match.
Match length is always `query.Length`.

**`FindAll`**: iterates every line, uses
`StringComparison.Ordinal` (case-sensitive) or
`StringComparison.OrdinalIgnoreCase` (case-insensitive), then applies the
whole-word check: the characters immediately before and after the match
(if they exist) must not satisfy `char.IsLetterOrDigit(c) || c == '_'`.

**`FindNext` / `FindPrev`**: call `FindAll`, locate the first result strictly
after / before `from`, wrap if needed.

**`BuildReplaceEvent`**: verifies the match still exists at `match` (query may
have changed), then returns an `InsertTextEvent` that deletes the matched range
and inserts `replacement`.

**`BuildReplaceAllEvents`**: collects all matches with `FindAll`, then builds
replace events in **reverse order** (last match first) so earlier positions are
not invalidated by later replacements. The caller publishes each event
individually so the undo stack gets one entry per replacement — or wraps them in
a future `CompositeEvent` if that exists.

> **Note**: For v1, Replace All publishes individual events. This means Ctrl+Z
> undoes one replacement at a time. A `CompositeEvent` to group them into a
> single undo step is a v2 follow-up.

### SearchBarView (Presentation layer)

A new `View` subclass, initially hidden (`Visible = false`). It sits between
`editorView` and `statusBar` in the layout, with a fixed height of 1 line
(Find-only) or 2 lines (Find + Replace).

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Find: [___________________________] [Aa] [W]  ◀ ▶  3 of 14 matches     │
│  Repl: [___________________________]  Replace   Replace All              │
└──────────────────────────────────────────────────────────────────────────┘
```

- **Find input** — `TextField`; typing triggers a live search and updates
  highlights + counter
- **`[Aa]`** — toggle button for case sensitivity; `Enabled` visual when active
- **`[W]`** — toggle button for whole-word matching
- **`◀ ▶`** — prev/next match buttons (also triggered by Shift+F3 / F3)
- **Match counter** — `Label`; shows "N of M matches", "1 match", or "No matches";
  appends " (Wrapped)" briefly (2 s timer) after a wrap-around
- **Replace input** — `TextField`; only visible in Replace mode
- **Replace / Replace All** — `Button`s; only visible in Replace mode

#### Opening and closing

| Action | Result |
|---|---|
| Ctrl+F | Open Find bar (Find-only height); focus Find input; Find input starts empty |
| F3 | If bar is open: Find Next. If bar is closed: reopen bar and advance to next match using last query |
| Ctrl+H | Open Find+Replace bar (2-line height); focus Find input |
| Escape | Close bar; return focus to editor |
| Ctrl+F while bar already open | Focus Find input (no-op if already focused) |
| Ctrl+H while Find-only bar open | Expand to Find+Replace |

When closed, `editorView.Height` fills the gap. The last query and options are
remembered for the session (F3/Shift+F3 reuse them after the bar is closed).

#### Match highlighting

After every search, `SearchBarView` raises a `SearchResultsChanged` event
carrying the full match list. `EditorView` subscribes and overlays a dim
highlight colour on all visible matches, overlaid by the normal selection colour
for the current match.

`EditorView.DrawSegment` checks each character position against the match list
using a fast range scan (matches are sorted by position). The current match is
highlighted with the existing `Selection` colour; other matches use a new
`IColorTheme.SearchMatch` colour pair.

#### Keyboard routing while bar is open

The Find/Replace inputs capture all printable keys and the following:
- `Enter` / `F3` / `Ctrl+F` — Find Next
- `Shift+F3` — Find Previous
- `Tab` — cycle focus between Find input → Replace input → buttons → Find input
- `Escape` — close bar

`EditorView.OnKeyDown` passes F3 / Shift+F3 / Ctrl+F through to `AppBootstrap`
handlers regardless of whether the bar is open (these keys are handled at the
window level, not inside the editor view).

### Color theme additions

`IColorTheme` gains one new property:

```csharp
ColorPair SearchMatch { get; }   // dim highlight for non-current matches
```

`DefaultDarkTheme` value: `new ColorPair(0, 3)` — black on dark-yellow.

### Layout changes (AppBootstrap)

```
Window
  MenuBar          Y = 0
  EditorView       Y = Bottom(menuBar),  Height = Fill() - searchBarHeight - 1
  SearchBarView    Y = AnchorEnd(1 + searchBarHeight),  Height = searchBarHeight
  StatusBar        Y = AnchorEnd(1),     Height = 1
```

`searchBarHeight` is 0 (hidden), 1 (Find), or 2 (Find+Replace).
`SearchBarView` notifies `AppBootstrap` when its height changes via a
`HeightChanged` event so `EditorView` can be re-laid-out.

### Events / state flow

```
User types in Find input
  → SearchBarView calls SearchService.FindAll
  → stores match list + current match index
  → raises SearchResultsChanged(matches, currentIndex)
  → EditorView redraws with highlights

User presses F3 / ▶
  → SearchBarView calls SearchService.FindNext from current match
  → updates current match index
  → publishes SetSelectionEvent to select the match in the buffer
  → raises SearchResultsChanged

User presses Replace
  → SearchBarView calls SearchService.BuildReplaceEvent
  → publishes the returned event via IEventBus
  → advances to next match

User presses Replace All
  → SearchBarView calls SearchService.BuildReplaceAllEvents
  → publishes each event via IEventBus
  → refreshes match list (should be empty)
```

## File layout

```
src/CodeEdit.Domain/
  IColorTheme.cs              — CHANGED: add SearchMatch property

src/CodeEdit.Application/
  SearchService.cs            — CHANGED: implement FindAll, FindNext, FindPrev,
                                BuildReplaceEvent, BuildReplaceAllEvents
  Events/                     — no new event types needed

src/CodeEdit.Infrastructure/
  Theme/DefaultDarkTheme.cs   — CHANGED: implement SearchMatch

src/CodeEdit.Presentation/
  Views/SearchBarView.cs      — NEW
  Views/EditorView.cs         — CHANGED: SearchResultsChanged handler,
                                match highlights in DrawSegment
  AppBootstrap.cs             — CHANGED: register SearchBarView, wire layout,
                                wire Ctrl+F / Ctrl+H / F3 / Shift+F3

tests/CodeEdit.Tests/
  Application/SearchServiceTests.cs  — NEW
```

## UI / UX

### Find bar (Ctrl+F)

```
  Find: [hello world              ] [Aa] [W]  ◀ ▶  3 of 14 matches
```

### Find + Replace bar (Ctrl+H)

```
  Find: [hello world              ] [Aa] [W]  ◀ ▶  3 of 14 matches
  Repl: [goodbye world            ]   Replace   Replace All
```

### No matches

```
  Find: [zzzzz                    ] [Aa] [W]  ◀ ▶  No matches
```

### Wrap indicator (shown for ~2 s)

```
  Find: [hello world              ] [Aa] [W]  ◀ ▶  3 of 14 matches (Wrapped)
```

## Open Questions

None.
