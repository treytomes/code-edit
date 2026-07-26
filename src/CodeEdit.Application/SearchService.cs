using System.Text.RegularExpressions;
using CodeEdit.Application.Events;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Application;

public sealed class SearchService(ILogger<SearchService> logger)
{
    public IReadOnlyList<CursorPosition> FindAll(
        ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
        Regex? regex = null)
    {
        if (string.IsNullOrEmpty(query) && regex is null) return [];

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var results    = new List<CursorPosition>();

        for (var lineIdx = 0; lineIdx < buffer.LineCount; lineIdx++)
        {
            var line = buffer.GetLine(lineIdx);
            if (regex is not null)
            {
                foreach (System.Text.RegularExpressions.Match m in regex.Matches(line))
                    results.Add(new CursorPosition(lineIdx, m.Index));
            }
            else
            {
                var col = 0;
                while (col <= line.Length - query.Length)
                {
                    var idx = line.IndexOf(query, col, comparison);
                    if (idx < 0) break;
                    if (!wholeWord || IsWholeWord(line, idx, query.Length))
                        results.Add(new CursorPosition(lineIdx, idx));
                    col = idx + 1;
                }
            }
        }

        logger.LogDebug("FindAll query={Query} matches={Count}", query, results.Count);
        return results;
    }

    public CursorPosition? FindNext(
        ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
        CursorPosition from, out bool wrapped, Regex? regex = null)
    {
        wrapped = false;
        var matches = FindAll(buffer, query, matchCase, wholeWord, regex);
        if (matches.Count == 0) return null;

        // First match strictly after `from`
        foreach (var m in matches)
        {
            if (m.Line > from.Line || (m.Line == from.Line && m.Column > from.Column))
                return m;
        }

        // Wrap around
        wrapped = true;
        return matches[0];
    }

    public CursorPosition? FindPrev(
        ITextBuffer buffer, string query, bool matchCase, bool wholeWord,
        CursorPosition from, out bool wrapped, Regex? regex = null)
    {
        wrapped = false;
        var matches = FindAll(buffer, query, matchCase, wholeWord, regex);
        if (matches.Count == 0) return null;

        // Last match strictly before `from`
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var m = matches[i];
            if (m.Line < from.Line || (m.Line == from.Line && m.Column < from.Column))
                return m;
        }

        // Wrap around
        wrapped = true;
        return matches[^1];
    }

    public (CursorPosition matchEnd, IBufferEvent ev) BuildReplaceEvent(
        ITextBuffer buffer, CursorPosition match,
        string query, string replacement, bool matchCase, bool wholeWord)
    {
        var line       = buffer.GetLine(match.Line);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Verify the match is still valid at this position
        if (match.Column + query.Length > line.Length
            || !line.AsSpan(match.Column, query.Length).Equals(query, comparison))
        {
            throw new InvalidOperationException("Match no longer valid at the specified position.");
        }

        if (wholeWord && !IsWholeWord(line, match.Column, query.Length))
            throw new InvalidOperationException("Match no longer valid at the specified position.");

        var rangeEnd  = new CursorPosition(match.Line, match.Column + query.Length);
        var range     = new TextRange(match, rangeEnd);
        var matchText = line.Substring(match.Column, query.Length);
        var ev        = new InsertTextEvent(match, replacement, range, matchText);

        // Calculate where cursor lands after replacement
        var replEnd = replacement.Contains('\n')
            ? EndPosition(match, replacement)
            : new CursorPosition(match.Line, match.Column + replacement.Length);

        return (replEnd, ev);
    }

    public (int count, IReadOnlyList<IBufferEvent> events) BuildReplaceAllEvents(
        ITextBuffer buffer, string query, string replacement,
        bool matchCase, bool wholeWord)
    {
        var matches = FindAll(buffer, query, matchCase, wholeWord);
        if (matches.Count == 0) return (0, []);

        // Build events in reverse order so earlier positions aren't shifted by later ones
        var events = new List<IBufferEvent>(matches.Count);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var (_, ev) = BuildReplaceEvent(buffer, matches[i], query, replacement, matchCase, wholeWord);
            events.Add(ev);
        }

        return (matches.Count, events);
    }

    private static bool IsWholeWord(string line, int start, int length)
    {
        if (start > 0 && IsWordChar(line[start - 1])) return false;
        var end = start + length;
        if (end < line.Length && IsWordChar(line[end])) return false;
        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static CursorPosition EndPosition(CursorPosition start, string text)
    {
        var line = start.Line;
        var col  = start.Column;
        foreach (var ch in text)
        {
            if (ch == '\n') { line++; col = 0; }
            else            { col++; }
        }
        return new CursorPosition(line, col);
    }
}
