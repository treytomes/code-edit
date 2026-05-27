# code-edit

TUI-based code editor in C#, targeting DOS EDIT feature parity as v1 and progressive VS Code parity as the long-term horizon.

## Stack

| Concern | Choice |
|---|---|
| Language | C# (.NET 9) |
| TUI framework | Terminal.Gui v2 |
| DI container | Microsoft.Extensions.DependencyInjection |
| Logging | Microsoft.Extensions.Logging → rolling file sink (`~/.code-edit/logs/`) |
| Test framework | xUnit |
| Target platforms | Linux, macOS, Windows |

## Development Process

This project uses **spec-driven development**:

1. Write a spec doc in `specs/` for the feature
2. Get explicit user sign-off on the spec
3. Implement against the spec — no more, no less
4. Write tests alongside the implementation

Never implement ahead of a spec. Never gold-plate beyond the spec scope.

## Project Structure

```
code-edit/
├── CodeEdit.sln                        # Solution file at repo root
├── specs/                              # Feature spec documents (written before implementation)
├── src/
│   ├── CodeEdit.Domain/                # Entities, value objects, core interfaces
│   ├── CodeEdit.Application/           # Use cases, event bus, ports
│   │   └── Ports/                      # Interfaces implemented by Infrastructure
│   ├── CodeEdit.Infrastructure/        # File buffer, syntax, theme, logging
│   │   ├── Buffer/
│   │   ├── Logging/
│   │   ├── Syntax/
│   │   └── Theme/
│   └── CodeEdit.Presentation/          # Terminal.Gui views, bootstrap
│       └── Views/
├── tests/
│   └── CodeEdit.Tests/                 # xUnit — tests Domain and Application only
├── .claude/
│   ├── agents/                         # Sub-agent specs
│   └── settings.local.json
└── CLAUDE.md
```

## Build & Test

```bash
# From repo root — always
dotnet build CodeEdit.sln
dotnet run --project src/CodeEdit.Presentation
dotnet test CodeEdit.sln
```

## Milestones

### v1 — DOS EDIT parity (current)
- [ ] Basic text editing (insert, delete, navigation)
- [ ] Syntax highlighting
- [ ] Menu system (File, Edit, Search, Help)
- [ ] Search and replace

### v2
- [ ] File tree panel
- [ ] Multiple tabs / buffers

### v3
- [ ] LSP integration
- [ ] Command palette

### v4
- [ ] Git gutter / integration

## Architecture Invariants

These are enforced at review time (grep checks in CI):

- `CodeEdit.Domain` — zero `Terminal.Gui` references
- `CodeEdit.Application` — zero `Terminal.Gui` references
- `CodeEdit.Infrastructure` — zero `Terminal.Gui` references
- `CodeEdit.Tests` — zero `Terminal.Gui` references
- `ColorPairMapper` is the **only** class that imports both `ColorPair` and `Terminal.Gui.Attribute`
- **Never** log to `Console` or `Debug.Write` — all logging via `ILogger<T>` to file sink only

## Coding Conventions

- Standard C# naming: `PascalCase` for types/members, `camelCase` for locals
- No XML doc comments on internal types — use self-documenting names
- Add a comment only when the WHY is non-obvious (a workaround, a hidden constraint, a subtle invariant)
- No premature abstractions — three similar lines is better than a wrong abstraction
- No error handling for scenarios that can't happen; validate only at system boundaries
- Prefer `readonly record struct` for small value types (cursor position, selection range, etc.)
- Terminal.Gui event handlers: always unsubscribe in `Dispose` to prevent leaks

## Terminal.Gui v2 Notes

### Namespaces (v2 uses sub-namespaces — not flat `Terminal.Gui`)
| Type | Namespace |
|---|---|
| `Application` | `Terminal.Gui.App` |
| `IApplication`, `IRunnable` | `Terminal.Gui.App` |
| `View` | `Terminal.Gui.ViewBase` |
| `Window`, `Dialog`, `ListView`, etc. | `Terminal.Gui.Views` |
| `Attribute`, `Color` | `Terminal.Gui.Drawing` |

### Naming conflict
`CodeEdit.Application` and `Terminal.Gui.App.Application` collide when both are in scope.
Always alias: `using TGuiApp = Terminal.Gui.App.Application;`

### Application lifecycle (v2 instance-based — old static API is obsolete)
```csharp
var app = TGuiApp.Create();
app.Init();
app.Run(window);       // blocks until RequestStop
app.RequestStop();     // stop (replaces old Shutdown)
```
`Application.Init()` / `Application.Run()` / `Application.Shutdown()` still compile but are marked `[Obsolete]`.

### Other rules
- All UI must run on the main thread; background work dispatches via `Application.Invoke`
- Prefer `Dim.Fill()` and `Pos.Relative()` over hardcoded positions
- `View.Draw()` is called by the framework — do not call it manually
- Use `IColorTheme` → `ColorPairMapper` for colors; never hardcode `Attribute` values in views

## Spec Document Format

Each spec in `specs/` should follow this structure:

```markdown
# Spec: <Feature Name>

## Status
Draft | Approved | Implemented

## Overview
One paragraph describing what this feature does and why.

## Scope
What is explicitly in scope and out of scope for this spec.

## Design
Technical design: data structures, algorithms, component interactions.

## UI / UX
How the feature presents in the TUI. Include ASCII mockups where helpful.

## Open Questions
Things not yet decided.
```
