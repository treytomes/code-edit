using System.Collections.Frozen;
using System.Text.RegularExpressions;
using CodeEdit.Application.Ports;
using CodeEdit.Domain;

namespace CodeEdit.Infrastructure.Syntax;

/// <summary>
/// Tokenizes lines using a loaded GrammarModel.
/// State encoding (opaque int passed between lines):
///   0         = normal
///   1..N      = inside block rule index N-1
///   N+1..N+M  = inside multiLine span rule index M-1 (within the span-rule subset)
///   Markdown fenced blocks: high bits encode a fenced-language index
/// </summary>
internal sealed class GrammarSyntaxProvider : ISyntaxProvider
{
    public string LanguageId  { get; }
    public string DisplayName { get; }

    // Compiled rules
    private abstract record CompiledRule(TokenType Token);
    private sealed record LineRule(Regex Pattern, TokenType Token)          : CompiledRule(Token);
    private sealed record BlockRule(Regex Open, Regex Close, TokenType Token, int State) : CompiledRule(Token);
    private sealed record SpanRule(string OpenLiteral, string CloseLiteral,
                                   char? EscapeChar, bool MultiLine,
                                   TokenType Token, int State)              : CompiledRule(Token);
    private sealed record KeywordsRule(FrozenSet<string> Words, TokenType Token) : CompiledRule(Token);
    private sealed record PatternRule(Regex Pattern, TokenType Token)       : CompiledRule(Token);
    private sealed record FencedCodeRule() : CompiledRule(TokenType.Default);

    private readonly List<CompiledRule> _rules;

    // Registry reference for fenced-code delegation (injected after construction)
    private GrammarRegistry? _registry;

    // Fenced-code state: high 16 bits = fenced-language slot (1-based), low 16 bits = 0xFFFF sentinel
    private const int FencedSentinel = 0x0000FFFF;
    private readonly Dictionary<string, int> _fencedSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string>            _fencedLanguages = [];

    internal GrammarSyntaxProvider(GrammarModel model)
    {
        LanguageId  = model.LanguageId;
        DisplayName = model.DisplayName;

        _rules = new List<CompiledRule>(model.Rules.Length);

        // Assign state values: block and multiLine-span rules get unique non-zero states
        var nextState = 1;

        foreach (var r in model.Rules)
        {
            var token = ParseToken(r.Token);

            switch (r.Type)
            {
                case "line":
                    _rules.Add(new LineRule(new Regex(r.Pattern!, RegexOptions.Compiled), token));
                    break;

                case "block":
                    _rules.Add(new BlockRule(
                        new Regex(r.Open!,  RegexOptions.Compiled),
                        new Regex(r.Close!, RegexOptions.Compiled),
                        token, nextState++));
                    break;

                case "span":
                {
                    var state = r.MultiLine ? nextState++ : 0;
                    char? esc = r.Escape is { Length: > 0 } e ? e[0] : null;
                    _rules.Add(new SpanRule(r.Open!, r.Close!, esc, r.MultiLine, token, state));
                    break;
                }

                case "keywords":
                    _rules.Add(new KeywordsRule(
                        r.Words.ToFrozenSet(StringComparer.Ordinal), token));
                    break;

                case "pattern":
                    _rules.Add(new PatternRule(
                        new Regex(r.Pattern!, RegexOptions.Compiled), token));
                    break;

                case "fenced-code":
                    _rules.Add(new FencedCodeRule());
                    break;
            }
        }
    }

    internal void SetRegistry(GrammarRegistry registry) => _registry = registry;

    public LineTokens TokenizeLine(string line, int lineIndex, int startState)
    {
        var tokens = new List<SyntaxToken>();

        // Fenced-code continuation
        if (IsFencedState(startState))
        {
            var langId  = FencedLanguageId(startState);
            var closing = IsFenceClose(line);
            if (closing)
                return new LineTokens(tokens, 0);

            // Delegate to inner provider if available
            var inner = _registry?.GetById(langId);
            if (inner is not null)
            {
                var lt = inner.TokenizeLine(line, lineIndex, 0);
                return new LineTokens(lt.Tokens, startState);
            }
            return new LineTokens(tokens, startState);
        }

        // Block-rule continuation (we're inside a block from a previous line)
        if (startState > 0)
        {
            var blockRule = FindRuleByState(startState);
            if (blockRule is BlockRule br)
            {
                var closeMatch = br.Close.Match(line);
                if (!closeMatch.Success)
                {
                    // Entire line is inside the block
                    tokens.Add(new SyntaxToken(lineIndex, 0, line.Length, br.Token));
                    return new LineTokens(tokens, startState);
                }
                var end = closeMatch.Index + closeMatch.Length;
                tokens.Add(new SyntaxToken(lineIndex, 0, end, br.Token));
                // Continue tokenizing the rest of the line in state 0
                var rest = TokenizeSegment(line, lineIndex, end, 0, tokens);
                return new LineTokens(tokens, rest);
            }

            if (blockRule is SpanRule sr)
            {
                var (consumed, newState) = ConsumeSpanTail(line, lineIndex, 0, sr, tokens, startState);
                if (newState == startState || newState != 0)
                {
                    // Still inside span — rest of line consumed, emit whole line as span token
                    tokens.Add(new SyntaxToken(lineIndex, 0, line.Length, sr.Token));
                    return new LineTokens(tokens, startState);
                }
                // Span closed on this line
                tokens.Add(new SyntaxToken(lineIndex, 0, consumed, sr.Token));
                var rest = TokenizeSegment(line, lineIndex, consumed, 0, tokens);
                return new LineTokens(tokens, rest);
            }
        }

        var endState = TokenizeSegment(line, lineIndex, 0, startState, tokens);
        return new LineTokens(tokens, endState);
    }

    // Returns the end state after tokenizing line[pos..] in the given state.
    private int TokenizeSegment(string line, int lineIndex, int pos, int state, List<SyntaxToken> tokens)
    {
        while (pos < line.Length)
        {
            // Check for fenced-code open (only in state 0)
            if (state == 0 && _rules.Any(r => r is FencedCodeRule))
            {
                var fenceMatch = TryMatchFenceOpen(line, pos);
                if (fenceMatch.HasValue)
                {
                    var (langTag, endPos) = fenceMatch.Value;
                    var slot  = GetOrAddFencedSlot(langTag);
                    var fState = EncodeFencedState(slot);
                    // Don't emit tokens for the fence line itself
                    return fState;
                }
            }

            var matched = false;
            foreach (var rule in _rules)
            {
                switch (rule)
                {
                    case FencedCodeRule:
                        continue;

                    case LineRule lr:
                    {
                        var m = lr.Pattern.Match(line, pos);
                        if (!m.Success || m.Index != pos) continue;
                        tokens.Add(new SyntaxToken(lineIndex, pos, m.Length, lr.Token));
                        pos    += m.Length;
                        matched = true;
                        break;
                    }

                    case BlockRule br:
                    {
                        var m = br.Open.Match(line, pos);
                        if (!m.Success || m.Index != pos) continue;
                        // Look for close on same line
                        var closeMatch = br.Close.Match(line, pos + m.Length);
                        if (closeMatch.Success)
                        {
                            var end = closeMatch.Index + closeMatch.Length;
                            tokens.Add(new SyntaxToken(lineIndex, pos, end - pos, br.Token));
                            pos    = end;
                            matched = true;
                        }
                        else
                        {
                            // Extends to end of line; carry state forward
                            tokens.Add(new SyntaxToken(lineIndex, pos, line.Length - pos, br.Token));
                            return br.State;
                        }
                        break;
                    }

                    case SpanRule sr:
                    {
                        if (!line.AsSpan(pos).StartsWith(sr.OpenLiteral.AsSpan())) continue;
                        var spanStart = pos;
                        pos += sr.OpenLiteral.Length;
                        var (endPos, newState) = ConsumeSpanTail(line, lineIndex, pos, sr, tokens, 0);
                        if (newState == SpanUnclosedSentinel)
                        {
                            // Unclosed single-line span: don't emit a token, advance past open char
                            pos = spanStart + 1;
                            matched = true;
                            break;
                        }
                        if (newState != 0)
                        {
                            // Unclosed multiLine span: emit and carry state
                            tokens.Add(new SyntaxToken(lineIndex, spanStart, line.Length - spanStart, sr.Token));
                            return sr.State;
                        }
                        tokens.Add(new SyntaxToken(lineIndex, spanStart, endPos - spanStart, sr.Token));
                        pos    = endPos;
                        matched = true;
                        break;
                    }

                    case KeywordsRule kr:
                    {
                        // Word boundary check
                        if (pos > 0 && IsWordChar(line[pos - 1])) continue;
                        foreach (var word in kr.Words)
                        {
                            if (pos + word.Length > line.Length) continue;
                            if (!line.AsSpan(pos, word.Length).SequenceEqual(word.AsSpan())) continue;
                            var after = pos + word.Length;
                            if (after < line.Length && IsWordChar(line[after])) continue;
                            tokens.Add(new SyntaxToken(lineIndex, pos, word.Length, kr.Token));
                            pos    += word.Length;
                            matched = true;
                            break;
                        }
                        if (matched) break;
                        continue;
                    }

                    case PatternRule pr:
                    {
                        // Match from full line to preserve \b semantics, filter to matches at pos
                        var m = pr.Pattern.Match(line, pos, line.Length - pos);
                        if (!m.Success || m.Index != pos) continue;
                        if (m.Length == 0) { pos++; matched = true; break; }
                        tokens.Add(new SyntaxToken(lineIndex, pos, m.Length, pr.Token));
                        pos    += m.Length;
                        matched = true;
                        break;
                    }
                }

                if (matched) break;
            }

            if (!matched) pos++;
        }

        return 0;
    }

    private const int SpanUnclosedSentinel = -1;

    // Consumes from pos inside a span until close delimiter or end-of-line.
    // Returns (endPos, newState):
    //   newState = 0                  → closed (delimiter found)
    //   newState = sr.State           → unclosed, multiLine (carry state to next line)
    //   newState = SpanUnclosedSentinel → unclosed, single-line (don't emit token)
    private (int endPos, int newState) ConsumeSpanTail(
        string line, int lineIndex, int pos, SpanRule sr,
        List<SyntaxToken> tokens, int continuationState)
    {
        while (pos < line.Length)
        {
            if (sr.EscapeChar.HasValue && line[pos] == sr.EscapeChar.Value && pos + 1 < line.Length)
            {
                pos += 2;
                continue;
            }
            if (line.AsSpan(pos).StartsWith(sr.CloseLiteral.AsSpan()))
            {
                pos += sr.CloseLiteral.Length;
                return (pos, 0);
            }
            pos++;
        }
        // Reached end of line without closing
        return (pos, sr.MultiLine ? sr.State : SpanUnclosedSentinel);
    }

    private CompiledRule? FindRuleByState(int state)
    {
        foreach (var rule in _rules)
        {
            if (rule is BlockRule br && br.State == state) return br;
            if (rule is SpanRule  sr && sr.State == state) return sr;
        }
        return null;
    }

    // ── Fenced-code helpers ────────────────────────────────────────────────

    private static readonly Regex FenceOpenRegex  = new(@"^(`{3,}|~{3,})\s*(\w*)", RegexOptions.Compiled);
    private static readonly Regex FenceCloseRegex = new(@"^(`{3,}|~{3,})\s*$",     RegexOptions.Compiled);

    private static (string langTag, int endPos)? TryMatchFenceOpen(string line, int pos)
    {
        if (pos != 0) return null;
        var m = FenceOpenRegex.Match(line);
        if (!m.Success) return null;
        return (m.Groups[2].Value, line.Length);
    }

    private static bool IsFenceClose(string line) => FenceCloseRegex.IsMatch(line);

    private static bool IsFencedState(int state) =>
        state > 0 && (state & 0xFFFF) == FencedSentinel;

    private static int EncodeFencedState(int slot) => (slot << 16) | FencedSentinel;

    private string FencedLanguageId(int state)
    {
        var slot = (state >> 16) - 1;
        return slot >= 0 && slot < _fencedLanguages.Count
            ? _fencedLanguages[slot]
            : string.Empty;
    }

    private int GetOrAddFencedSlot(string langId)
    {
        if (_fencedSlots.TryGetValue(langId, out var existing)) return existing;
        _fencedLanguages.Add(langId);
        var slot = _fencedLanguages.Count; // 1-based
        _fencedSlots[langId] = slot;
        return slot;
    }

    // ── Utilities ──────────────────────────────────────────────────────────

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static TokenType ParseToken(string s) => s switch
    {
        "Keyword"       => TokenType.Keyword,
        "StringLiteral" => TokenType.StringLiteral,
        "CharLiteral"   => TokenType.CharLiteral,
        "Comment"       => TokenType.Comment,
        "Number"        => TokenType.Number,
        "Operator"      => TokenType.Operator,
        "Punctuation"   => TokenType.Punctuation,
        "Identifier"    => TokenType.Identifier,
        _               => TokenType.Default,
    };
}
