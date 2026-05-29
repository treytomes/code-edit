# code-edit

A lightweight TUI code editor for Linux, macOS, and Windows. Targets DOS EDIT feature parity as v1, with progressive VS Code parity as the long-term horizon.

Built with [Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui) and .NET 10.

## Features (v1)

- **Text editing** — insert, delete, undo/redo, clipboard (cut/copy/paste)
- **Navigation** — cursor movement, word jump, scroll, select all, mouse support
- **Search and replace** — find, find next/previous, replace, replace all; case-sensitive and whole-word options
- **Syntax highlighting** — C#, Python, Bash, JSON, YAML, .env, Markdown; user grammar overrides at `~/.code-edit/syntaxes/`
- **File operations** — new, open, save, save as, recent files
- **Word wrap** toggle
- **Help** — keyboard shortcut reference (F1), About

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Building and running

```bash
# Build
dotnet build CodeEdit.sln

# Run (opens an empty buffer)
dotnet run --project src/CodeEdit.Presentation

# Run with a file
dotnet run --project src/CodeEdit.Presentation -- path/to/file.cs

# Tests
dotnet test CodeEdit.sln
```

## Keyboard shortcuts

| Key | Action |
|-----|--------|
| Ctrl+N | New file |
| Ctrl+O | Open file |
| Ctrl+S | Save |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / Copy / Paste |
| Ctrl+A | Select all |
| Tab / Shift+Tab | Indent / Dedent selection |
| Ctrl+F | Find |
| F3 / Shift+F3 | Find next / previous |
| Ctrl+H | Find and replace |
| Ctrl+Left/Right | Word left / right |
| Ctrl+Up/Down | Scroll up / down |
| Alt+Z | Toggle word wrap |
| F1 | Keyboard shortcuts reference |
| Esc | Close search bar |

## Project structure

```
code-edit/
├── specs/                  # Feature spec documents
├── src/
│   ├── CodeEdit.Domain/        # Entities and value objects
│   ├── CodeEdit.Application/   # Use cases and event bus
│   ├── CodeEdit.Infrastructure/# File I/O, syntax, theme, logging
│   └── CodeEdit.Presentation/  # Terminal.Gui views and bootstrap
└── tests/
    └── CodeEdit.Tests/         # xUnit tests
```

Logs are written to `~/.code-edit/logs/`. User grammar files go in `~/.code-edit/syntaxes/`.
