# Spec: Syntax Highlighting

## Status
Draft

## Overview
Provide per-language syntax coloring for the seven languages most used in this project: C#, Python, Bash, JSON, YAML, .env, and Markdown. Grammars are stored as JSON resource files so new languages can be added by dropping a file into a well-known directory without rebuilding the application. Tokenization is stateful line-by-line (carrying an opaque integer "start state" across lines) to handle block comments and multi-line strings correctly. Token results are cached per logical line and invalidated forward from the first affected line on every buffer mutation.

## Scope

**In scope**
- Stateful line-by-line tokenizer engine driven by JSON grammar files
- Seven built-in grammars embedded as assembly resources: C#, Python, Bash, JSON, YAML, .env, Markdown
- User grammar override directory at `~/.code-edit/syntaxes/`
- `GrammarRegistry` replacing `ExtensionShebangSyntaxDetector` as the grammar selector
- Per-line token cache in `EditorView` with forward-invalidation on edit
- `BufferMutated(int firstAffectedLine)` event on the event bus to drive invalidation
- Dark Modern 16-color theme mapping for all token types
- Markdown fenced code blocks: if the fenced language is in the registry, delegate inner-line tokenization to that grammar

**Out of scope**
- Semantic highlighting (LSP, type-aware coloring)
- Incremental/background re-tokenization (re-tokenization happens synchronously on draw)
- Syntax-aware indentation or bracket matching
- Themes other than the built-in Dark Modern mapping
- Grammar hot-reload while the app is running (grammars are loaded once at startup)

## Design

### Interface changes

`ISyntaxProvider` is reshaped to a stateful single-line API:

```csharp
public interface ISyntaxProvider
{
    /// <summary>
    /// Tokenize one line. startState is 0 for "normal"; other values are
    /// opaque to callers — only the provider that produced them interprets them.
    /// </summary>
    LineTokens TokenizeLine(string line, int lineIndex, int startState);
}

public readonly record struct LineTokens(
    IReadOnlyList<SyntaxToken> Tokens,
    int EndState);
```

`PlainTextSyntaxProvider` always returns `EndState = 0` and an empty token list.

The old `Tokenize(IReadOnlyList<string> lines, int startLineIndex)` overload is removed.

### Grammar file format

Each grammar is a UTF-8 JSON file named `<languageId>.json`.

```jsonc
{
  "languageId": "csharp",
  "displayName": "C#",
  "extensions": [".cs"],
  "shebangs": [],
  "rules": [ /* ordered; first match wins */ ]
}
```

#### Rule types

**`line`** — matches a regex to the end of line; never crosses a newline.
```jsonc
{ "type": "line", "pattern": "//.*$", "token": "Comment" }
```

**`block`** — open/close regex pair that carries state across lines.
The engine assigns each block rule a unique non-zero state integer at load time.
```jsonc
{ "type": "block", "open": "/\\*", "close": "\\*/", "token": "Comment" }
```

**`span`** — open/close delimiters within a line. When `multiLine` is `true` the span behaves like a `block` rule (state carried across lines). An unclosed span at end-of-line with `multiLine: false` reverts to `Default` — no state is carried forward.
```jsonc
{ "type": "span", "open": "\"", "close": "\"", "escape": "\\\\",
  "multiLine": false, "token": "StringLiteral" }
```

**`keywords`** — exact word-boundary matches checked before general patterns.
```jsonc
{ "type": "keywords",
  "words": ["class", "void", "return", "if", "else"],
  "token": "Keyword" }
```

**`pattern`** — arbitrary single-line regex.
```jsonc
{ "type": "pattern", "pattern": "\\b[0-9]+(\\.[0-9]+)?\\b", "token": "Number" }
```

**`fenced-code`** (Markdown only) — matches the opening fence `` ``` `` or `~~~`, captures the language tag, looks it up in the grammar registry, and delegates inner-line tokenization to that provider. Carries a composite state `InFencedBlock(languageId)` across lines until the closing fence. Falls back to `Default` coloring if the inner language is not in the registry.
```jsonc
{ "type": "fenced-code" }
```

#### Rule evaluation order
Rules are evaluated top-to-bottom. The engine scans each character position; the first rule whose pattern matches at that position wins. Keyword matching is word-boundary exact and is checked before `pattern` rules.

### Grammar registry (`GrammarRegistry`)

Replaces `ExtensionShebangSyntaxDetector` and `ISyntaxDetector`.

Load order at startup:
1. Embedded resources in `CodeEdit.Infrastructure` — the seven built-in grammars.
2. User files in `~/.code-edit/syntaxes/*.json` — loaded after built-ins; a user file with the same `languageId` replaces the built-in.

Selection logic (`Select(string? filePath, string? firstLine) → ISyntaxProvider`):
1. Match by file extension (case-insensitive) against all loaded grammars.
2. If no extension match, inspect `firstLine` for a shebang (`#!`) and match against `shebangs` lists.
3. If still no match, return `PlainTextSyntaxProvider`.

### Token cache and invalidation

`EditorView` owns a `LineTokenCache` (a resizable array of `CachedLine?`):

```csharp
private record struct CachedLine(int StartState, IReadOnlyList<SyntaxToken> Tokens, int EndState);
```

**Invalidation**: A new event `BufferMutatedEvent(int firstAffectedLine)` is published on the event bus immediately after any mutating event (`InsertTextEvent`, `DeleteRangeEvent`) executes. `EditorView` subscribes and tracks `_invalidateFrom = Math.Min(_invalidateFrom, firstAffectedLine)`. The cache array is not cleared immediately — stale entries past `_invalidateFrom` are overwritten lazily during the next draw pass.

**Draw-time re-tokenization**: For each visible logical line `i`:
1. Determine `startState`: line 0 always has `startState = 0`; otherwise use the `EndState` from the cache entry for line `i-1` (or 0 if that entry is absent/invalid).
2. If the cache entry for line `i` exists and its `StartState` matches the computed `startState`, use cached tokens.
3. Otherwise call `TokenizeLine`, store the result, and continue to the next line.
4. Forward re-tokenization stops when the new `EndState` matches the previously cached `EndState` for that line (convergence), or when the visible window is exhausted.

**Cache resize**: When the buffer is replaced (`SetBuffer`), the cache is cleared and resized to the new `LineCount`.

### Built-in grammar definitions

#### C# (csharp) — `.cs`
Rules (in order):
1. `line`: `//.*$` → Comment
2. `block`: `/\*` … `\*/` → Comment
3. `span`: `@"` … `"` (multiLine: false, no escape) → StringLiteral — verbatim strings
4. `span`: `"` … `"` (escape: `\\`, multiLine: false) → StringLiteral
5. `span`: `'` … `'` (escape: `\\`, multiLine: false) → CharLiteral
6. `keywords`: `abstract`, `as`, `async`, `await`, `base`, `bool`, `break`, `byte`, `case`, `catch`, `char`, `checked`, `class`, `const`, `continue`, `decimal`, `default`, `delegate`, `do`, `double`, `else`, `enum`, `event`, `explicit`, `extern`, `false`, `finally`, `fixed`, `float`, `for`, `foreach`, `goto`, `if`, `implicit`, `in`, `int`, `interface`, `internal`, `is`, `lock`, `long`, `namespace`, `new`, `null`, `object`, `operator`, `out`, `override`, `params`, `private`, `protected`, `public`, `readonly`, `record`, `ref`, `return`, `sbyte`, `sealed`, `short`, `sizeof`, `stackalloc`, `static`, `string`, `struct`, `switch`, `this`, `throw`, `true`, `try`, `typeof`, `uint`, `ulong`, `unchecked`, `unsafe`, `ushort`, `using`, `virtual`, `void`, `volatile`, `while`, `var`, `yield` → Keyword
7. `pattern`: `\b[0-9]+(\.[0-9]+)?(f|d|m|L|UL|u)?\b` → Number
8. `pattern`: `[+\-*/%&|^~<>=!]+` → Operator
9. `pattern`: `[(){}\[\],;.]` → Punctuation

#### Python (python) — `.py`, shebangs: `python`, `python3`
Rules (in order):
1. `line`: `#.*$` → Comment
2. `span`: `"""` … `"""` (multiLine: true) → StringLiteral
3. `span`: `'''` … `'''` (multiLine: true) → StringLiteral
4. `span`: `"` … `"` (escape: `\\`, multiLine: false) → StringLiteral
5. `span`: `'` … `'` (escape: `\\`, multiLine: false) → StringLiteral
6. `keywords`: `False`, `None`, `True`, `and`, `as`, `assert`, `async`, `await`, `break`, `class`, `continue`, `def`, `del`, `elif`, `else`, `except`, `finally`, `for`, `from`, `global`, `if`, `import`, `in`, `is`, `lambda`, `nonlocal`, `not`, `or`, `pass`, `raise`, `return`, `try`, `while`, `with`, `yield` → Keyword
7. `pattern`: `\b[0-9]+(\.[0-9]+)?\b` → Number
8. `pattern`: `[+\-*/%&|^~<>=!@]+` → Operator
9. `pattern`: `[(){}\[\],;.:]` → Punctuation

#### Bash (bash) — `.sh`, `.bash`, shebangs: `bash`, `sh`
Rules (in order):
1. `line`: `#.*$` → Comment
2. `span`: `"` … `"` (escape: `\\`, multiLine: false) → StringLiteral
3. `span`: `'` … `'` (multiLine: false, no escape) → StringLiteral
4. `pattern`: `\$\{[A-Za-z_][A-Za-z0-9_]*\}` → Identifier
5. `pattern`: `\$[A-Za-z_][A-Za-z0-9_]*` → Identifier
6. `keywords`: `case`, `do`, `done`, `elif`, `else`, `esac`, `fi`, `for`, `function`, `if`, `in`, `return`, `select`, `then`, `until`, `while`, `local`, `export`, `readonly`, `declare`, `source`, `exit`, `echo`, `true`, `false` → Keyword
7. `pattern`: `\b[0-9]+\b` → Number
8. `pattern`: `[(){}\[\];|&<>]` → Punctuation

#### JSON (json) — `.json`
Rules (in order):
1. `span`: `"` … `"` (escape: `\\`, multiLine: false) → StringLiteral
2. `keywords`: `true`, `false`, `null` → Keyword
3. `pattern`: `-?\b[0-9]+(\.[0-9]+)?([eE][+\-]?[0-9]+)?\b` → Number
4. `pattern`: `[{}\[\]:,]` → Punctuation

#### YAML (yaml) — `.yaml`, `.yml`
Rules (in order):
1. `line`: `#.*$` → Comment
2. `span`: `"` … `"` (escape: `\\`, multiLine: false) → StringLiteral
3. `span`: `'` … `'` (multiLine: false, no escape) → StringLiteral
4. `pattern`: `^[ \t]*[A-Za-z_][A-Za-z0-9_\-]*(?=\s*:)` → Keyword (map keys)
5. `keywords`: `true`, `false`, `null`, `yes`, `no`, `on`, `off` → Keyword
6. `pattern`: `-?\b[0-9]+(\.[0-9]+)?\b` → Number
7. `pattern`: `[:{}\[\],\-]` → Punctuation

#### .env (dotenv) — `.env`
Rules (in order):
1. `line`: `#.*$` → Comment
2. `pattern`: `^[A-Za-z_][A-Za-z0-9_]*(?=\s*=)` → Keyword (variable name before `=`)
3. `span`: `"` … `"` (escape: `\\`, multiLine: false) → StringLiteral
4. `span`: `'` … `'` (multiLine: false, no escape) → StringLiteral
5. `pattern`: `[A-Za-z0-9_./@\-]+` → StringLiteral (unquoted value text)

#### Markdown (markdown) — `.md`, `.markdown`
Rules (in order):
1. `fenced-code` (delegates inner lines to registry lookup by language tag)
2. `line`: `^#{1,6}\s.*$` → Keyword (headings)
3. `line`: `^>.*$` → Comment (blockquotes)
4. `span`: `` ` `` … `` ` `` (multiLine: false) → StringLiteral (inline code)
5. `line`: `^\s*[-*+]\s` → Punctuation (unordered list markers)
6. `line`: `^\s*[0-9]+\.\s` → Punctuation (ordered list markers)
7. `pattern`: `\*\*.*?\*\*` → Keyword (bold)
8. `pattern`: `\*.*?\*` → Identifier (italic)
9. `pattern`: `\[.*?\]\(.*?\)` → StringLiteral (links)

### Dark Modern color mapping

`DefaultDarkTheme.ForToken` returns:

| TokenType | Terminal color index | Name |
|---|---|---|
| Keyword | 12 | BrightBlue |
| StringLiteral | 9 | BrightRed (salmon) |
| CharLiteral | 9 | BrightRed |
| Comment | 2 | DarkGreen |
| Number | 10 | BrightGreen |
| Operator | 15 | BrightWhite |
| Punctuation | 8 | DarkGray |
| Identifier | 15 | BrightWhite |
| Default | 15 | BrightWhite |

Background is always 0 (Black) for all token colors.

## UI / UX

No visible UI change beyond the colored text. The status bar already shows the detected language name (via `DetectedLanguage` on the buffer / future registry lookup); that field should be wired to the grammar's `displayName` once the registry is in place.

There is no user-facing toggle for syntax highlighting in v1.

## File layout

New and changed files:

```
src/CodeEdit.Domain/
  SyntaxToken.cs              — unchanged
  TokenType.cs                — unchanged
  LineTokens.cs               — NEW: LineTokens record struct

src/CodeEdit.Application/
  Ports/ISyntaxProvider.cs    — CHANGED: stateful single-line API
  Events/BufferMutatedEvent.cs — NEW

src/CodeEdit.Infrastructure/
  Syntax/
    GrammarRegistry.cs        — NEW: replaces ExtensionShebangSyntaxDetector
    GrammarSyntaxProvider.cs  — NEW: engine that runs a loaded grammar
    GrammarLoader.cs          — NEW: JSON deserialization + embedded resource loading
    PlainTextSyntaxProvider.cs — CHANGED: updated to new ISyntaxProvider signature
    ExtensionShebangSyntaxDetector.cs — REMOVED
  Theme/
    DefaultDarkTheme.cs       — CHANGED: ForToken returns distinct colors
  Resources/
    csharp.json
    python.json
    bash.json
    json.json
    yaml.json
    dotenv.json
    markdown.json

src/CodeEdit.Presentation/
  Views/EditorView.cs         — CHANGED: token cache, invalidation subscription
  AppBootstrap.cs             — CHANGED: register GrammarRegistry instead of detector
```

## Open Questions

None — all design questions resolved prior to spec writing.
