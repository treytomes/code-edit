# Spec: Theme Editor

## Status
Draft

## Overview
An in-app TUI dialog that lets the user view and edit the active color theme. Changes are saved to `~/.code-edit/themes/current.json` and applied immediately. The user can also reset to the built-in VS Code Dark+ defaults.

## Scope

**In scope**
- `ThemeEditorDialog` — modal dialog accessible from View > Edit Theme…
- A scrollable list of named color roles (Normal, Selection, TabBar, FileTree, StatusBar, MenuBar, LineNumber, SearchMatch, and all token types)
- Each row shows the role name, a foreground color swatch, and a background color swatch
- Clicking or pressing Enter on a swatch opens an inline RGB editor (R/G/B fields, 0–255)
- Live preview: changes apply to the active theme and redraw the editor immediately
- Save to `~/.code-edit/themes/current.json`; loaded automatically on next launch
- Reset to defaults button: restores built-in VS Code Dark+ values (in memory and on disk)
- `ThemeService` in `CodeEdit.Infrastructure.Settings` handles load/save

**Out of scope**
- Named theme presets / switching between multiple named themes (v4+)
- Import/export of `.json` theme files via file picker (v4+)
- Font or font-size settings
- Syntax grammar editing

## Design

### Theme file

Location: `~/.code-edit/themes/current.json`

```json
{
  "normal":      { "fg": "#D4D4D4", "bg": "#1E1E1E" },
  "selection":   { "fg": "#FFFFFF", "bg": "#264F78" },
  "lineNumber":  { "fg": "#858585", "bg": "#1E1E1E" },
  "statusBar":   { "fg": "#FFFFFF", "bg": "#007ACC" },
  "menuBar":     { "fg": "#FFFFFF", "bg": "#007ACC" },
  "tabBar":      { "fg": "#D4D4D4", "bg": "#2D2D2D" },
  "fileTree":    { "fg": "#D4D4D4", "bg": "#252526" },
  "dialog":      { "fg": "#D4D4D4", "bg": "#252526" },
  "searchMatch": { "fg": "#D4D4D4", "bg": "#613315" },
  "tokens": {
    "keyword":       { "fg": "#569CD6", "bg": "#1E1E1E" },
    "stringLiteral": { "fg": "#CE9178", "bg": "#1E1E1E" },
    "charLiteral":   { "fg": "#CE9178", "bg": "#1E1E1E" },
    "comment":       { "fg": "#6A9955", "bg": "#1E1E1E" },
    "number":        { "fg": "#B5CEA8", "bg": "#1E1E1E" },
    "operator":      { "fg": "#D4D4D4", "bg": "#1E1E1E" },
    "punctuation":   { "fg": "#808080", "bg": "#1E1E1E" },
    "identifier":    { "fg": "#DCDCAA", "bg": "#1E1E1E" }
  }
}
```

Colors are stored as CSS hex strings (`#RRGGBB`). Alpha is always 255 and not stored.

### `ThemeService`

```csharp
public sealed class ThemeService(ILogger<ThemeService> logger, string? settingsDir = null)
{
    public IColorTheme Load();              // returns DefaultDarkTheme if file absent or malformed
    public void Save(IColorTheme theme);    // serializes to current.json
}
```

- `settingsDir` defaults to `~/.code-edit/themes/`; test-isolation pattern matches existing services.
- `Save()` writes atomically (`.tmp` → rename).
- `Load()` logs a warning and falls back to `DefaultDarkTheme` on any error.

### `UserTheme`

A mutable concrete implementation of `IColorTheme` whose properties are settable:

```csharp
public sealed class UserTheme : IColorTheme
{
    public ColorPair Normal      { get; set; }
    public ColorPair Selection   { get; set; }
    public ColorPair LineNumber  { get; set; }
    public ColorPair StatusBar   { get; set; }
    public ColorPair MenuBar     { get; set; }
    public ColorPair TabBar      { get; set; }
    public ColorPair FileTree    { get; set; }
    public ColorPair Dialog      { get; set; }
    public ColorPair SearchMatch { get; set; }

    // Token colors stored per-type
    private Dictionary<TokenType, ColorPair> _tokens;
    public ColorPair ForToken(TokenType type)
        => _tokens.TryGetValue(type, out var p) ? p : Normal;

    // Construct from DefaultDarkTheme values (used as defaults)
    public static UserTheme FromDefaults() => new(new DefaultDarkTheme());
    public UserTheme(IColorTheme source); // copies all values
}
```

### `ThemeEditorDialog`

```csharp
public sealed class ThemeEditorDialog : Dialog
{
    public ThemeEditorDialog(ThemeRegistry themeRegistry, ThemeService themeService);
}
```

Layout (80×24 dialog):

```
┌─ Theme Editor ─────────────────────────────────────────────────────┐
│ Role              Foreground        Background                      │
│ ──────────────────────────────────────────────────────────────────  │
│ Normal            ██ #D4D4D4        ██ #1E1E1E                     │
│ Selection         ██ #FFFFFF        ██ #264F78                      │
│ Line Number       ██ #858585        ██ #1E1E1E                      │
│ ...                                                                  │
│ ──────────────────────────────────────────────────────────────────  │
│ ┌─ Edit: Normal › Foreground ─────────────────────────────────────┐ │
│ │  R [ 212 ]   G [ 212 ]   B [ 212 ]   Preview: ████████          │ │
│ └─────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│              [ Reset to Defaults ]  [ Cancel ]  [ Save ]            │
└──────────────────────────────────────────────────────────────────────┘
```

- The scrollable list uses `ListView`; each row displays the role name + two colored `Label` swatches (`██`)
- Selecting a row and pressing Enter (or clicking a swatch) opens the inline RGB editor panel at the bottom of the dialog
- The RGB editor shows three `TextField` inputs (0–255), validated on change; the preview swatch updates live
- Every valid change immediately calls `themeRegistry.SetTheme(workingCopy)` so the rest of the app redraws in real time — the working copy is a `UserTheme` cloned from the current theme at dialog open
- **Save**: writes working copy to disk via `ThemeService.Save()`, leaves it active
- **Cancel**: restores the original theme (`themeRegistry.SetTheme(original)`) without saving
- **Reset to Defaults**: replaces working copy with `UserTheme.FromDefaults()`, applies live, does not auto-save

### `ThemeRegistry` changes

```csharp
public void SetTheme(IColorTheme theme); // replaces Active and fires ThemeChanged
```

`ThemeChanged` is already defined; `SetTheme` is the missing mutator.

### `AppBootstrap` integration

- Register `ThemeService` in DI.
- On launch: call `themeService.Load()` and pass result to `ThemeRegistry` via `SetTheme()` before the window opens.
- Add View menu item: `View > Edit Theme…` opens `ThemeEditorDialog`.

### Tests

- `ThemeService`: load returns `DefaultDarkTheme` when file absent; save/load round-trips all roles; malformed JSON falls back to defaults; `settingsDir` isolation.
- `UserTheme`: `FromDefaults()` matches `DefaultDarkTheme` values; copy constructor copies all fields; `ForToken` falls back to Normal for unknown type.

## Open Questions
None.
