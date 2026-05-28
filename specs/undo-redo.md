# Spec: Undo / Redo

## Status
Implemented

## Overview
Expose the undo/redo stacks that `EventBus` already manages via keyboard shortcuts
and Edit menu items. Menu items are disabled when the respective stack is empty.

## Scope

**In scope**
- `Ctrl+Z` — undo last event
- `Ctrl+Y` / `Ctrl+Shift+Z` — redo last undone event
- Edit menu items: "Undo" (Ctrl+Z) and "Redo" (Ctrl+Y), disabled when unavailable
- Menu item titles update to reflect stack state (e.g. "Undo Typing", "Undo Delete")
- Clearing undo/redo stacks when a new buffer is loaded (already handled by `EventBus.SetBuffer`)

**Out of scope**
- Undo history panel / visualiser
- Per-file undo stacks when multiple buffers are open (v2+)
- Grouping multiple events into a named transaction

## Design

### Keyboard handling (EditorView)

Add three key cases to `OnKeyDown`:

| Key | Action |
|---|---|
| `Ctrl+Z` | `if (_eventBus.CanUndo) _eventBus.Undo()` |
| `Ctrl+Y` | `if (_eventBus.CanRedo) _eventBus.Redo()` |
| `Ctrl+Shift+Z` | `if (_eventBus.CanRedo) _eventBus.Redo()` |

All three set `key.Handled = true` and return `true` regardless of stack state
(consuming the key prevents Terminal.Gui from doing anything else with it).

### Menu items (AppBootstrap)

`MenuItem` in Terminal.Gui v2 accepts a `CanExecute` delegate (`Func<bool>`) that
controls whether the item renders as enabled or disabled. Wire it to the event bus:

```csharp
var undoItem = new MenuItem("_Undo", "Ctrl+Z", DoUndo,
    canExecute: () => eventBus.CanUndo);
var redoItem = new MenuItem("_Redo", "Ctrl+Y", DoRedo,
    canExecute: () => eventBus.CanRedo);
```

Add both to the `_Edit` menu, above the existing Cut/Copy/Paste block, separated
by a `null` divider:

```
Edit
  Undo        Ctrl+Z
  Redo        Ctrl+Y
  ──────────────────
  Cut         Ctrl+X
  Copy        Ctrl+C
  Paste       Ctrl+V
```

`DoUndo` and `DoRedo` are simple lambdas:

```csharp
void DoUndo() { if (eventBus.CanUndo) eventBus.Undo(); }
void DoRedo() { if (eventBus.CanRedo) eventBus.Redo(); }
```

### Menu refresh

Terminal.Gui v2 evaluates `CanExecute` each time the menu opens, so no explicit
refresh call is needed.

## File layout

Changed files only:

```
src/CodeEdit.Presentation/
  AppBootstrap.cs   — add Undo/Redo menu items to Edit menu
  Views/EditorView.cs — add Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z handlers
```

No new files. No changes to Domain, Application, or Infrastructure.

## UI / UX

```
┌─ Edit ─────────────────────┐
│ Undo          Ctrl+Z       │  ← greyed when stack empty
│ Redo          Ctrl+Y       │  ← greyed when stack empty
│ ─────────────────────────  │
│ Cut           Ctrl+X       │
│ Copy          Ctrl+C       │
│ Paste         Ctrl+V       │
└────────────────────────────┘
```

## Open Questions

None.
