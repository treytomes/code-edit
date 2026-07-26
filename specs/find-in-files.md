# Spec: Find in Files

## Status
Implemented

## Overview
Find in Files extends the editor's existing single-file search with cross-file search across the open workspace folder. Ctrl+Alt+F opens a modal dialog to enter a query and options; results populate a `FindResultsPanel` in the left sidebar under a new "Search" tab. Pressing Enter on a result opens the file in a new tab and jumps to the matched line. F3/Shift+F3 advance through cross-file results when the last search context was a Find-in-Files run. The existing inline search bar (Ctrl+F) gains a regex toggle to complete the search feature set at this milestone.

## Scope

### In scope
- `FindInFilesDialog` — modal, Ctrl+Alt+F, query + glob filter + option toggles
- `FindInFilesService` in `CodeEdit.Infrastructure` — synchronous search across files
- `FindResultsPanel` — new `View` that displays a file/match tree in the sidebar
- Left sidebar gains a two-tab strip: "Files" and "Search"; only one panel is visible at a time
- Click or Enter on a result opens the file in a new tab (or activates an existing tab) and positions the cursor at the matched line
- F3/Shift+F3 advancing through cross-file results when the active search context is Find-in-Files
- `SearchContext` — shared state that tracks which search (inline or find-in-files) owns F3
- Regex toggle (`[.*]` button) added to the existing `SearchBarView`
- Regex pattern errors surfaced inline in the search bar (counter label shows error text)
- Search menu gains `Find in Files  Ctrl+Alt+F` and a disabled `Replace in Files` placeholder
- Keyboard shortcuts help text updated to include new bindings
- `bin/`, `obj/`, `.git/` directories always skipped; binary files skipped (null-byte check)

### Out of scope
- Replace in Files (deferred; menu item present but disabled)
- Live/incremental file-watching during a search
- `**` glob patterns (only simple `*.ext`-style globs via `FileSystemName.MatchesSimpleExpression`; no new NuGet dependency)
- Resizable sidebar
- Search history / saved queries
- Search scope narrowing (e.g. open files only, custom exclude patterns beyond hard-coded skips)
- `.gitignore` parsing (only the three hard-coded skips apply)

## Design

### Data structures

```csharp
// CodeEdit.Infrastructure/Search/FindInFilesOptions.cs
public sealed record FindInFilesOptions(
    bool   CaseSensitive = false,
    bool   WholeWord     = false,
    bool   UseRegex      = false,
    string FileGlob      = "");

// CodeEdit.Infrastructure/Search/LineMatch.cs
public sealed record LineMatch(
    int    LineNumber,   // 0-based
    string LineText,
    int    MatchColumn,  // 0-based column of match start
    int    MatchLength);

// CodeEdit.Infrastructure/Search/FileMatches.cs
public sealed record FileMatches(
    string                   FilePath,
    IReadOnlyList<LineMatch>  Matches);
```

### Services

#### `FindInFilesService` (Infrastructure)

Registered as a singleton. Operates synchronously; the workspace is assumed to be a local filesystem with small-to-medium file counts.

```csharp
public sealed class FindInFilesService(ILogger<FindInFilesService> logger)
{
    // Search all text files under rootDir for query.
    // Returns one FileMatches entry per file that has at least one match.
    // Files are processed in directory-walk order (depth-first, dirs before files,
    // case-insensitive sort within each group — consistent with FileTreeView).
    public IReadOnlyList<FileMatches> Search(
        string            rootDir,
        string            query,
        FindInFilesOptions options);

    // Determines whether a file is binary by reading the first 8 KB
    // and checking for any null byte (0x00).
    private static bool IsBinary(string filePath);
}
```

**Directory skip list** (checked against each directory name, not the full path):

```csharp
private static readonly HashSet<string> SkippedDirs =
    new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git" };
```

**Match logic**: uses `StringComparison.Ordinal` (case-sensitive) or `StringComparison.OrdinalIgnoreCase` (case-insensitive). Whole-word check: the character immediately before the match start and immediately after the match end (if they exist) must not satisfy `char.IsLetterOrDigit(c) || c == '_'` — identical to the rule in `SearchService`. Multiple matches per line are all recorded.

**Regex support**: when `UseRegex` is `true`, the query is compiled as a `System.Text.RegularExpressions.Regex` (with `RegexOptions.IgnoreCase` added when `CaseSensitive` is `false`). If the pattern is invalid, `Search` throws `ArgumentException` and the caller shows an error dialog. When `UseRegex` is `true`, `WholeWord` is ignored.

**`FileGlob`**: if non-empty, the file name (not full path) is tested with `FileSystemName.MatchesSimpleExpression(fileName, options.FileGlob, ignoreCase: true)` from `System.IO.Enumeration`. Simple `*.ext` and `prefix*` patterns are supported; `**` recursive patterns are not. Files that do not match are skipped; directories are always descended.

**Result cap**: `Search` stops accumulating matches once the total across all files reaches 1000. When truncated, the returned list includes a sentinel: the last `FileMatches` entry has `FilePath = ""` and a single `LineMatch` with `LineText = "Results truncated at 1000 matches — refine your query."`. `FindResultsPanel` renders this sentinel as a notice row rather than a navigable result.

**Error handling**: per-file `IOException` is logged at Debug and the file is skipped; does not abort the entire search.

#### `SearchContext` (Application)

Tracks which search last ran and owns F3 navigation.

```csharp
public enum SearchContextKind { None, InlineFind, FindInFiles }

public sealed class SearchContext
{
    public SearchContextKind        Kind             { get; private set; } = SearchContextKind.None;
    public IReadOnlyList<FileMatches> FindInFilesResults { get; private set; } = [];
    public int                      FileIndex        { get; private set; } = 0;
    public int                      MatchIndex       { get; private set; } = 0;

    // Called by SearchBarView when the inline bar runs a search.
    public void SetInlineContext();

    // Called by AppBootstrap after FindInFilesService.Search completes.
    public void SetFindInFilesContext(IReadOnlyList<FileMatches> results);

    // Advance to the next result. Returns null if no results remain.
    // Wraps from the last match of the last file back to the first.
    public (string filePath, LineMatch match, bool wrapped)? MoveNext();

    // Advance to the previous result. Wraps similarly.
    public (string filePath, LineMatch match, bool wrapped)? MovePrev();
}
```

`SearchContext` is registered as a singleton in the DI container. `SearchBarView` receives it and calls `SetInlineContext()` on every `RunSearch()`. F3/Shift+F3 handlers in `AppBootstrap` check `SearchContext.Kind` to decide whether to delegate to `SearchBarView` (inline) or to `FindResultsPanel` (find-in-files).

### UI components

#### `SidebarView` (Presentation)

A new container `View` that wraps `FileTreeView` and `FindResultsPanel` with a two-tab strip at the top.

```csharp
public sealed class SidebarView : View
{
    public event EventHandler? FilesPanelSelected;
    public event EventHandler? SearchPanelSelected;

    public void ShowFilesPanel();
    public void ShowSearchPanel();

    public SidebarTab ActiveTab { get; private set; }

    public enum SidebarTab { Files, Search }
}
```

The tab strip occupies the top 1 row of `SidebarView`. `FileTreeView` and `FindResultsPanel` each occupy `Height = Fill() - 1` below it. Only the active panel's `Visible` is `true`.

Tab strip rendering (in `OnDrawingContent`):

```
 Files  Search
 ─────
```

The active tab name is underlined (drawn with the Selection color pair); the inactive tab uses the Normal color pair. A horizontal rule (U+2500 × tab width) is drawn beneath the active tab label.

Mouse click on a tab name switches the active panel. Keyboard: Left/Right arrows while the tab strip row has focus; or the sidebar always passes focus directly to the active panel's content area.

#### `FindResultsPanel` (Presentation)

```csharp
public sealed class FindResultsPanel : View
{
    public event EventHandler<OpenResultArgs>? ResultOpenRequested;

    // Clears results and shows the empty-state message.
    public void Clear();

    // Populates the panel with search results.
    // queryDisplay is shown in a header row: e.g. "hello (14 matches in 3 files)"
    public void SetResults(string queryDisplay, IReadOnlyList<FileMatches> results);

    // Selects the result at (fileIndex, matchIndex) and scrolls it into view.
    public void HighlightResult(int fileIndex, int matchIndex);
}

public sealed record OpenResultArgs(string FilePath, int LineNumber);
```

Internal flat row list (similar to `FileTreeView`'s `_entries`):

```csharp
private sealed record ResultRow(
    RowKind Kind,
    int     FileIndex,    // index into _results
    int     MatchIndex,   // -1 for file header rows
    string  DisplayText);

private enum RowKind { FileHeader, Match }
```

**Rendering**: file header rows use a distinct color (e.g. `IColorTheme.FileTree` color pair, bold if the theme supports it); match rows are indented 2 spaces, use the Normal color pair. The selected row uses the Selection color pair.

**Keyboard**: arrows to navigate rows, Enter to fire `ResultOpenRequested`. F3/Shift+F3 are not handled inside `FindResultsPanel` directly — they are handled at the window level by `AppBootstrap` using `SearchContext.MoveNext/MovePrev`.

#### Regex toggle on `SearchBarView`

A `[.*]` `Button` (or `CheckBox`) is added to the find row, to the right of the `[W]` whole-word toggle and to the left of the `◀ ▶` nav buttons. When toggled on, `RunSearch()` compiles the query as a `System.Text.RegularExpressions.Regex` before searching. If the pattern is invalid, `_counterLabel.Text` is set to `"Invalid regex"` and matches are cleared; the field is not tinted (Terminal.Gui v2 does not support per-field background color overrides at this layer).

`SearchService` gains two new overloads that accept a pre-compiled `Regex?`:

```csharp
public IReadOnlyList<CursorPosition> FindAll(
    ITextBuffer buffer, string query, bool matchCase, bool wholeWord, Regex? regex = null);

public CursorPosition? FindNext(
    ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
    CursorPosition from, out bool wrapped, Regex? regex = null);

public CursorPosition? FindPrev(
    ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
    CursorPosition from, out bool wrapped, Regex? regex = null);
```

When `regex` is non-null the `query` and `matchCase`/`wholeWord` parameters are ignored for matching; match length is derived from `match.Length` rather than `query.Length`.

### AppBootstrap wiring

New DI registrations (added after `app.Init()`):

```csharp
services.AddSingleton<FindInFilesService>();
services.AddSingleton<SearchContext>();
services.AddSingleton<FindResultsPanel>();
services.AddSingleton<SidebarView>();
```

`FileTreeView` is no longer placed directly in the window; it is owned by `SidebarView`. `AppBootstrap` wires sidebar dimensions identically to the old file-tree dimensions:

```
sidebarView.X      = 0
sidebarView.Y      = Pos.Bottom(tabBar)
sidebarView.Width  = Dim.Absolute(TreeWidth)   // 30
sidebarView.Height = Dim.Fill() - Dim.Absolute(1)
```

`SetTreeVisible` is renamed `SetSidebarVisible`. The auto-hide width check uses `TreeWidth + MinEditorWidth` as before. `treeUserVisible` becomes `sidebarUserVisible`.

New event handlers:

```csharp
editorView.FindInFilesRequested += (_, _) => DoFindInFiles();

void DoFindInFiles()
{
    // 1. Open FindInFilesDialog
    var dlg = new FindInFilesDialog();
    app.Run(dlg);
    if (dlg.Canceled) return;

    // 2. Run search (regex compile failure shows error dialog and aborts)
    IReadOnlyList<FileMatches> results;
    try { results = findInFilesService.Search(rootDir, dlg.Query, dlg.Options); }
    catch (ArgumentException ex) { MessageBox.Query(app, "Invalid Regex", ex.Message, "OK"); return; }

    // 3. Update SearchContext
    searchContext.SetFindInFilesContext(results);

    // 4. Populate panel and switch sidebar to Search tab
    var total = results.Sum(f => f.Matches.Count);
    var fileCount = results.Count;
    var summary = $"{dlg.Query}  ({total} match{(total == 1 ? "" : "es")} in {fileCount} file{(fileCount == 1 ? "" : "s")})";
    findResultsPanel.SetResults(summary, results);
    sidebar.ShowSearchPanel();
    SetSidebarVisible(true);
    sidebarUserVisible = true;
    findResultsPanel.SetFocus();
}
```

F3/Shift+F3 at the window level (already handled in `editorView.FindNextRequested` / `editorView.FindPrevRequested`) gain a context check:

```csharp
editorView.FindNextRequested += (_, _) =>
{
    if (searchContext.Kind == SearchContextKind.FindInFiles)
        AdvanceFindInFiles(forward: true);
    else if (searchBar.CurrentMode == SearchBarView.Mode.Closed)
        searchBar.Open(SearchBarView.Mode.Find, resetQuery: false);
    else
        searchBar.NavigateNext();
};
```

`AdvanceFindInFiles` calls `searchContext.MoveNext()`, opens or activates the file tab, jumps the cursor, and calls `findResultsPanel.HighlightResult(fileIndex, matchIndex)`.

Opening a result from `FindResultsPanel.ResultOpenRequested` (Enter or click):

```csharp
findResultsPanel.ResultOpenRequested += (_, args) =>
    DoOpenFindResult(args.FilePath, args.LineNumber);

void DoOpenFindResult(string filePath, int lineNumber)
{
    // Activate existing tab if file is already open; otherwise open new tab
    var existingIndex = eventBus.Buffers.Tabs
        .Select((t, i) => (t, i))
        .FirstOrDefault(x => string.Equals(x.t.Buffer.FilePath, filePath,
                             StringComparison.OrdinalIgnoreCase)).i;
    // ... open or activate, then publish SetCursorEvent to line lineNumber col 0
    editorView.SetFocus();
}
```

`editorView` gains a new event:

```csharp
public event EventHandler? FindInFilesRequested;  // fired on Ctrl+Alt+F in OnKeyDown
```

Ctrl+B while the sidebar is visible on the Search tab closes the sidebar as before (no change to toggle logic).

### Menu changes

Search menu after this spec:

```
_Search
  _Find               Ctrl+F
  Find _Next          F3
  Find _Previous      Shift+F3
  _Replace            Ctrl+H
  ─────────────────────────────────
  Find in _Files      Ctrl+Alt+F
  Replace in Files    (disabled)
```

## UI / UX

### Find in Files dialog (60 × 12)

```
┌─ Find in Files ──────────────────────────────────────────┐
│                                                          │
│  Query:   [____________________________________]         │
│                                                          │
│  Filter:  [*.cs_______________________________]         │
│           (file glob, e.g. *.cs or src/**)               │
│                                                          │
│  [ ] Case sensitive    [ ] Whole word    [ ] Regex       │
│                                                          │
│                         [ Cancel ]  [ Search ]           │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

- Tab order: Query → Filter → Case sensitive → Whole word → Regex → Cancel → Search
- Enter in any field activates Search
- Escape cancels
- Query field is focused on open; current editor selection is pre-filled if it is single-line and under 200 characters; multi-line selections are ignored (empty pre-fill)

### Sidebar tab strip

```
┌──────────────────────────────┐
│ Files  Search                │
│ ─────                        │   ← underline under active tab
│ src/                         │
│  ▶ CodeEdit.Domain/          │
│  ▼ CodeEdit.Application/     │
│    ...                       │
└──────────────────────────────┘
```

```
┌──────────────────────────────┐
│ Files  Search                │
│        ──────                │   ← Search tab active
│ "hello"  (6 matches, 2 files)│
│ ─────────────────────────────│
│ Program.cs  (4 matches)      │
│   12: string hello = "hello" │
│   47:     hello = Greet()    │
│   51:     return hello;      │
│   89: // says hello          │
│ Readme.md  (2 matches)       │
│   3: # hello world           │
│   9: Say hello to the user   │
└──────────────────────────────┘
```

### FindResultsPanel — empty state

```
┌──────────────────────────────┐
│ Files  Search                │
│        ──────                │
│                              │
│  Press Ctrl+Alt+F          │
│  to search across files.     │
│                              │
└──────────────────────────────┘
```

### SearchBarView with regex toggle

```
Find: [____________________] [Aa] [W] [.*]  ◀ ▶  No matches
```

When regex is active and the pattern is invalid:

```
Find: [****[invalid_______] [Aa] [W] [.*]  ◀ ▶  Invalid regex
```

(`[.*]` appears visually toggled / highlighted when active.)

### Keybindings

| Key | Action |
|-----|--------|
| Ctrl+Alt+F | Open Find in Files dialog (from editor or anywhere) |
| Ctrl+B | Toggle sidebar (Files or Search tab, whichever was last active) |
| Enter (on result row) | Open file at matched line, focus editor |
| F3 | Next match — cross-file if last search was Find in Files |
| Shift+F3 | Previous match — cross-file if last search was Find in Files |
| Escape (in sidebar) | Return focus to editor |
| Left / Right | Switch sidebar tab when tab strip has focus |

## Test Cases

### `FindInFilesService`

1. Empty query returns an empty result list.
2. Query with no matches in any file returns an empty result list.
3. Query matching text in three files returns three `FileMatches` entries, each with correct `LineNumber`, `MatchColumn`, `MatchLength`, and `LineText`.
4. Case-insensitive search matches upper- and lower-case variants; case-sensitive search does not.
5. Whole-word search does not match the query when it appears as a substring of a longer word (e.g. `"he"` does not match `"hello"`).
6. Whole-word search matches when the query is surrounded by non-word characters or by line boundaries.
7. A file containing a null byte in the first 8 KB is skipped and does not appear in results.
8. Files inside `bin/`, `obj/`, and `.git/` directories are not included in results.
9. `FileGlob = "*.cs"` returns only `.cs` files; a `.txt` file with a matching line is excluded.
10. Empty `FileGlob` returns results from all text files regardless of extension.
11. Multiple matches on the same line are all recorded as separate `LineMatch` entries.
12. A file that cannot be read (permissions error) is skipped; the search continues and returns results from other files.
13. The result list is ordered depth-first, directories before files, case-insensitive within each group — matching `FileTreeView` ordering.
14. `UseRegex = true` with a valid pattern matches regex patterns (e.g. `hel+o` matches `hello`).
15. `UseRegex = true` with an invalid pattern throws `ArgumentException`.
16. `UseRegex = true` ignores the `WholeWord` option.
17. When total matches reach 1000, the sentinel `FileMatches` entry (empty `FilePath`) is appended and no further matches are collected.

### `SearchContext`

14. `Kind` is `None` initially; `SetInlineContext()` sets it to `InlineFind`; `SetFindInFilesContext(...)` sets it to `FindInFiles`.
15. `MoveNext()` returns `null` when `Kind` is `None` or results are empty.
16. `MoveNext()` advances through all matches in a file before moving to the next file.
17. `MoveNext()` wraps from the last match of the last file to the first match of the first file, setting `wrapped = true`.
18. `MovePrev()` wraps from the first match of the first file to the last match of the last file.
19. After `SetFindInFilesContext` with new results, `FileIndex` and `MatchIndex` reset to 0.

### `SearchBarView` regex toggle

20. When regex is off, a literal query containing regex metacharacters (e.g. `"hello.world"`) matches literal text only.
21. When regex is on, `"hello.world"` matches `"helloXworld"` (dot matches any character).
22. An invalid regex pattern (e.g. `"[unclosed"`) sets the counter label to `"Invalid regex"` and clears highlights.
23. Toggling regex off after an invalid pattern re-runs the search as a literal query and restores normal match display.

### Integration (AppBootstrap wiring)

24. Ctrl+Alt+F opens `FindInFilesDialog`; canceling the dialog does not alter `FindResultsPanel` state.
25. After a successful search, the sidebar switches to the Search tab and `FindResultsPanel` shows the summary header and result rows.
26. Pressing Enter on a file-header row in `FindResultsPanel` opens the file and positions the cursor at the first match in that file.
27. Pressing Enter on a match row opens the file and positions the cursor at that specific line.
28. If the file is already open in a tab, activating a result switches to that tab rather than opening a duplicate.
29. F3 after a Find-in-Files search advances to the next result cross-file; opening each file as needed.
30. F3 after an inline search runs single-file navigation as before (no regression).
31. Ctrl+B closes the sidebar regardless of which tab is active.
32. Auto-hide still hides the sidebar (including the Search tab) when the terminal is narrower than 60 columns.

## Open Questions

None.
