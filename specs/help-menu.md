# Spec: Help Menu

## Status
Implemented

## Overview
Add a Help menu to the menu bar with two items: a keyboard shortcut reference dialog and an About dialog. This gives new users a discoverable reference for key bindings without leaving the app, and satisfies the DOS EDIT parity goal of a Help menu entry.

## Scope

**In scope**
- `_Help` menu bar item with two entries: `_Keyboard Shortcuts` and `_About`
- Keyboard Shortcuts dialog: scrollable list of all key bindings, grouped by category
- About dialog: app name, version string, brief description

**Out of scope**
- Context-sensitive help
- Hyperlinks or external browser launch
- Help for individual menu items (tooltip-style)

## Design

Both dialogs are implemented as `Dialog` instances shown via `app.Run(dialog)` in AppBootstrap, consistent with how `OpenDialog` and `SaveDialog` are used today. No new types are required.

### Keyboard Shortcuts dialog

A `Dialog` sized to roughly 60×22 (width × height) containing a read-only `TextView` with all shortcuts. `TextView.ReadOnly = true`; the user can scroll with arrow keys. Dismissed with Escape or an OK button.

Content (text rendered into the TextView):

```
FILE
  Ctrl+N          New file
  Ctrl+O          Open file
  Ctrl+S          Save

EDIT
  Ctrl+Z          Undo
  Ctrl+Y          Redo
  Ctrl+X          Cut
  Ctrl+C          Copy
  Ctrl+V          Paste
  Ctrl+A          Select all
  Tab             Indent selection
  Shift+Tab       Dedent selection

SEARCH
  Ctrl+F          Find
  F3              Find next
  Shift+F3        Find previous
  Ctrl+H          Find and replace

NAVIGATION
  Ctrl+Left/Right Word left / right
  Ctrl+Up/Down    Scroll up / down
  Home / End      Line start / end
  Ctrl+Home/End   Document start / end

VIEW
  Alt+Z           Toggle word wrap
```

### About dialog

A `MessageBox.Query` (or equivalent `Dialog`) with:

```
code-edit  v<assembly-version>

A lightweight TUI code editor.
```

Version is read from `Assembly.GetExecutingAssembly().GetName().Version` at runtime, formatted as `major.minor` (e.g. `1.0`). Single "OK" button. No scrolling needed.

## UI / UX

Help menu in the menu bar:

```
 File  Edit  Search  View  Help
                            ├ Keyboard Shortcuts  F1
                            └ About
```

F1 opens the Keyboard Shortcuts dialog (DOS EDIT convention). The binding is handled in `EditorView.OnKeyDown` alongside the other global keys. No other new global key bindings are added.

### Keyboard Shortcuts dialog mockup

```
┌─ Keyboard Shortcuts ──────────────────────────────────────┐
│ FILE                                                       │
│   Ctrl+N          New file                                 │
│   Ctrl+O          Open file                                │
│   Ctrl+S          Save                                     │
│                                                            │
│ EDIT                                                       │
│   Ctrl+Z          Undo                                     │
│   Ctrl+Y          Redo                                     │
│   Ctrl+X          Cut                                      │
│   Ctrl+C          Copy                                     │
│   Ctrl+V          Paste                                    │
│   Ctrl+A          Select all                               │
│   Tab             Indent selection                         │
│   Shift+Tab       Dedent selection                         │
│                                                            │
│ SEARCH                                                     │
│   Ctrl+F          Find                                     │
│   F3              Find next                                │
│   Shift+F3        Find previous                            │
│   Ctrl+H          Find and replace                         │
│ ──────────────────────────────────────────────────── ↓    │
│                         [ OK ]                             │
└────────────────────────────────────────────────────────────┘
```

### About dialog mockup

```
┌─ About ──────────────────────┐
│                              │
│   code-edit  v1.0.0.0        │
│                              │
│   A lightweight TUI code     │
│   editor.                    │
│                              │
│           [ OK ]             │
└──────────────────────────────┘
```

## Open Questions

None.
