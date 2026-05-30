# Spec: Theme Editor

## Status
Draft

## Overview
An in-app TUI dialog for browsing, creating, editing, and deleting named color themes. Themes are stored as individual JSON files in `~/.code-edit/themes/`. The active theme name is persisted in `settings.json`. Users can export any theme to an arbitrary path and import theme files from the file tree or a file-picker dialog.

## Scope

**In scope**
- `ThemeEditorDialog` — modal dialog, View > Edit Theme…
- Theme list panel: all themes in `~/.code-edit/themes/`, with the active theme marked
- Color role editor: scrollable list of named roles; clicking/entering a swatch opens an inline RGB editor
- Live preview: changes apply immediately via `ThemeRegistry.SetTheme()`
- Per-theme actions: New (clone + rename prompt), Rename, Duplicate (clone + rename prompt), Delete (built-in protected), Save, Revert
- Export: write the selected theme to a user-chosen path via Save dialog
- Import: open a `.json` file via Open dialog and add it to `~/.code-edit/themes/`; reject files that don't parse as valid themes
- `ThemeService` in `CodeEdit.Infrastructure.Settings`
- Active theme name stored in `settings.json` as `"activeTheme"`; loaded at launch
- Error dialogs for all failable operations (Import, Export, Save, Delete, Rename collisions)
- Tests for `ThemeService` and `UserTheme`

**Out of scope**
- Online theme gallery / marketplace
- Syntax grammar editing
- Font or font-size settings
- Theme file watching / live reload from disk

## Design

### Theme file format

Each theme is a standalone `.json` file in `~/.code-edit/themes/`:

```json
{
  "name": "VS Code Dark+",
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

Colors are CSS hex strings (`#RRGGBB`). The file name on disk is a sanitised slug of the theme name (e.g. `vs-code-dark-plus.json`); the `"name"` field is the display name. The built-in VS Code Dark+ theme is written to this directory on first launch if no themes exist yet.

### `settings.json` addition

```json
{
  "tabWidth": 4,
  "insertSpaces": true,
  "recentFilesMax": 10,
  "activeTheme": "VS Code Dark+"
}
```

`activeTheme` is matched against the `"name"` field of loaded theme files. If absent or unmatched, the built-in `DefaultDarkTheme` is used as a fallback.

### `EditorSettings` addition

```csharp
public sealed record EditorSettings(
    int TabWidth,
    bool InsertSpaces,
    int RecentFilesMax,
    string? ActiveTheme = null);
```

### `UserTheme`

Mutable concrete `IColorTheme` used for editing and for loaded user themes:

```csharp
public sealed class UserTheme : IColorTheme
{
    public string Name { get; set; }

    public ColorPair Normal      { get; set; }
    public ColorPair Selection   { get; set; }
    public ColorPair LineNumber  { get; set; }
    public ColorPair StatusBar   { get; set; }
    public ColorPair MenuBar     { get; set; }
    public ColorPair TabBar      { get; set; }
    public ColorPair FileTree    { get; set; }
    public ColorPair Dialog      { get; set; }
    public ColorPair SearchMatch { get; set; }

    private Dictionary<TokenType, ColorPair> _tokens;
    public ColorPair ForToken(TokenType type)
        => _tokens.TryGetValue(type, out var p) ? p : Normal;

    public static UserTheme FromDefaults(string name = "VS Code Dark+");
    public UserTheme Clone(string? newName = null);   // deep copy, optionally renamed
    public UserTheme(IColorTheme source, string name); // copy constructor
}
```

### `ThemeService`

```csharp
public sealed class ThemeService(ILogger<ThemeService> logger, string? themesDir = null)
{
    // Returns all themes from the themes directory.
    // Seeds the directory with the built-in theme if empty.
    public IReadOnlyList<UserTheme> LoadAll();

    // Loads a single theme by name (matched on "name" field).
    // Returns null if not found.
    public UserTheme? LoadByName(string name);

    // Saves (or overwrites) a theme. File name derived from theme.Name.
    public void Save(UserTheme theme);

    // Deletes a theme file by name. No-op if not found.
    public void Delete(string name);

    // Exports a theme to an arbitrary path.
    public void Export(UserTheme theme, string destinationPath);

    // Imports a theme file from an arbitrary path into the themes directory.
    // Returns the imported theme, or throws ThemeImportException if invalid.
    public UserTheme Import(string sourcePath);
}
```

`themesDir` defaults to `~/.code-edit/themes/`. Test-isolation pattern matches existing services.

### `ThemeRegistry` addition

```csharp
public void SetTheme(IColorTheme theme);  // replaces Active, fires ThemeChanged
```

### `ThemeEditorDialog`

Opens as an 80×24 modal dialog.

```
┌─ Theme Editor ──────────────────────────────────────────────────────────┐
│ Themes                    │ Role              Fg            Bg           │
│ ─────────────────────     │ ────────────────────────────────────────     │
│ ● VS Code Dark+           │ Normal            ██ #D4D4D4    ██ #1E1E1E  │
│   My Custom Theme         │ Selection         ██ #FFFFFF    ██ #264F78  │
│   Solarized Dark          │ Line Number       ██ #858585    ██ #1E1E1E  │
│                           │ Status Bar        ██ #FFFFFF    ██ #007ACC  │
│                           │ Menu Bar          ██ #FFFFFF    ██ #007ACC  │
│ [ New ] [ Dupe ] [ Del ]  │ Tab Bar           ██ #D4D4D4    ██ #2D2D2D  │
│ [ Import ] [ Export ]     │ File Tree         ██ #D4D4D4    ██ #252526  │
│                           │ ── Tokens ──────────────────────────────    │
│                           │ Keyword           ██ #569CD6    ██ #1E1E1E  │
│                           │ ...                                          │
│                           ├─────────────────────────────────────────────│
│                           │ R [ 212 ]  G [ 212 ]  B [ 212 ]  ████████   │
└───────────────────────────┴────────────[ Revert ]  [ Cancel ]  [ Save ]─┘
```

- **Left panel**: scrollable list of theme names; `●` marks the active theme; `[ New ]`, `[ Dupe ]`, `[ Del ]`, `[ Import ]`, `[ Export ]` buttons below
- **Right panel**: scrollable role list; selecting a swatch (Tab/Enter or click) opens the inline RGB editor at the bottom
- **Live preview**: every valid RGB change immediately calls `themeRegistry.SetTheme(workingCopy)`; the editor behind the dialog redraws in real time

#### Name prompt dialog

New, Dupe, and Rename all share the same inline name prompt:

```
┌─ New Theme Name ─────────────────────┐
│ Name: [ My Custom Theme            ] │
│               [ Cancel ]  [ OK ]     │
└──────────────────────────────────────┘
```

Validation runs on OK: name must be non-empty and not already exist (case-insensitive). If invalid, an error message appears inside the prompt dialog and it stays open.

#### Button behaviours

- **New**: opens name prompt; on confirm, clones the currently selected theme under the new name, adds it to the list, selects it, and marks it unsaved
- **Dupe**: identical to New — opens name prompt, clones, selects, marks unsaved
- **Rename**: opens name prompt pre-filled with current name; on confirm, renames the working copy (built-in theme: button disabled)
- **Del**: disabled for the built-in theme; shows confirmation dialog (`"Delete '{name}'? This cannot be undone." [ Delete ] [ Cancel ]`); on confirm, deletes the file and selects the next theme in the list (or the built-in if the list is now empty)
- **Import**: opens a file-picker dialog (`.json` filter); on parse failure shows an error dialog; on name collision shows the name prompt pre-filled with the imported name so the user can rename before adding
- **Export**: opens a save dialog pre-filled with the theme's slug filename; on failure shows an error dialog
- **Save**: saves working copy to disk; on failure shows an error dialog; updates `settings.json` `activeTheme` if the saved theme is the active one
- **Revert**: discards unsaved edits to the selected theme, restores from disk (or built-in defaults); re-applies to registry; no confirmation (edits not yet saved are simply lost)
- **Cancel**: restores the original theme that was active when the dialog opened; no disk writes

#### Error dialog

All failable operations (Save, Export, Import, Delete) catch exceptions and display:

```
┌─ Error ──────────────────────────────────────────────────────┐
│ Could not save theme: Access to path '...' is denied.        │
│                                    [ OK ]                    │
└──────────────────────────────────────────────────────────────┘
```

`MessageBox.Query` is used for this; the message is `ex.Message`.

### Name validation and slug generation

- Theme names must be non-empty and unique (case-insensitive among loaded themes).
- Slug: lowercase, spaces → hyphens, strip non-alphanumeric except hyphens. E.g. `"My Theme!"` → `my-theme.json`.
- If a rename would produce a slug that collides with an existing file for a *different* theme (e.g. two themes slugging to the same filename), the validation error is shown in the name prompt.
- Import name collision: shows the name prompt pre-filled with the imported theme's name; user can adjust and confirm, or cancel the import entirely.

### `AppBootstrap` integration

- Register `ThemeService` in DI (after `app.Init()`).
- On launch: call `themeService.LoadAll()` to seed the directory; then `themeService.LoadByName(editorSettings.ActiveTheme)` and apply via `themeRegistry.SetTheme()` if found.
- View menu: `View > Edit Theme…` → opens `ThemeEditorDialog`.

### Tests

**`ThemeService`**
- `LoadAll` seeds built-in theme when directory is empty.
- `Save` / `LoadByName` round-trips all color roles and name.
- `Delete` removes the file; subsequent `LoadByName` returns null.
- `Export` writes a valid theme file to the given path.
- `Import` loads a valid file and adds it to the themes directory.
- `Import` throws `ThemeImportException` on malformed JSON.
- `Import` throws `ThemeImportException` on name collision.
- `themesDir` isolation (temp directory).

**`UserTheme`**
- `FromDefaults()` matches `DefaultDarkTheme` values for all roles.
- `Clone()` produces a deep copy; modifying the clone does not affect the original.
- `Clone(newName)` sets the clone's name without affecting the original's name.
- `ForToken` falls back to `Normal` for an unrecognised token type.
- Copy constructor copies all fields including name.

## Open Questions
None.
