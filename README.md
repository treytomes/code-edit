# code-edit

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/treytomes)

![CI](https://github.com/treytomes/code-edit/actions/workflows/ci.yml/badge.svg)

A lightweight TUI code editor for Linux, macOS, and Windows. Targets DOS EDIT feature parity as v1, with progressive VS Code parity as the long-term horizon.

Built with [Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui) and .NET 10.

## Features (v2)

- **Multiple tabs** — open files as tabs, close with ×, Ctrl+Tab / Ctrl+Shift+Tab to cycle, Ctrl+W to close
- **File tree panel** — Ctrl+B to toggle; keyboard and mouse navigation; F5 to refresh
- **Session persistence** — open tabs and active file restored on next launch (`.code-edit/session.json`)
- **Per-tab undo/redo** — history is independent per tab
- **Text editing** — insert, delete, undo/redo, clipboard (cut/copy/paste)
- **Navigation** — cursor movement, word jump, scroll, select all, mouse support
- **Search and replace** — find, find next/previous, replace, replace all; case-sensitive and whole-word options
- **Syntax highlighting** — C#, Python, Bash, JSON, YAML, XML/csproj, .env, Markdown; user grammar overrides at `~/.code-edit/syntaxes/`
- **Theme editor** — View > Edit Theme… to browse, create, edit, duplicate, rename, import, and export named color themes stored in `~/.code-edit/themes/`
- **File operations** — new, open file, open folder, save, save as, recent files and folders
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

# Coverage report (generates HTML at coverage/report/index.html)
./scripts/coverage.sh
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
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+W | Close tab |
| Ctrl+B | Toggle file tree |
| Ctrl+F | Find |
| F3 / Shift+F3 | Find next / previous |
| Ctrl+H | Find and replace |
| Ctrl+Left/Right | Word left / right |
| Ctrl+Up/Down | Scroll up / down |
| Alt+Z | Toggle word wrap |
| F1 | Keyboard shortcuts reference |
| Esc | Close search bar / return focus from file tree |

## Project structure

```
code-edit/
├── specs/                  # Feature spec documents
├── src/
│   ├── CodeEdit.Domain/        # Entities and value objects
│   ├── CodeEdit.Application/   # Use cases and event bus
│   ├── CodeEdit.Infrastructure/# File I/O, syntax, theme, logging
│   └── CodeEdit.Presentation/  # Terminal.Gui views and bootstrap
├── tests/
│   └── CodeEdit.Tests/         # xUnit tests
└── scripts/
    └── coverage.sh             # Local HTML coverage report
```

Logs are written to `~/.code-edit/logs/`. User grammar files go in `~/.code-edit/syntaxes/`. Session data is written to `.code-edit/session.json` in the working directory.

## Code coverage

Coverage is collected on every CI run (excluding the Presentation layer, which requires a running terminal). A minimum of **75% line coverage** is enforced. The full HTML report is uploaded as a CI artifact on every build.

To generate the report locally:

```bash
./scripts/coverage.sh
```