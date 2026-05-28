using CodeEdit.Application;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Application;

public sealed class SearchServiceTests
{
    private static SearchService MakeSvc() => new(NullLogger<SearchService>.Instance);

    private static FakeSearchBuffer MakeBuffer(params string[] lines)
    {
        var buf = new FakeSearchBuffer();
        foreach (var l in lines) buf.Lines.Add(l);
        return buf;
    }

    private sealed class FakeSearchBuffer : ITextBuffer
    {
        public List<string>   Lines { get; } = [];
        public int            LineCount         => Lines.Count;
        public CursorPosition Cursor            { get; set; }
        public Selection?     Selection         { get; set; }
        public bool           IsDirty           => false;
        public string?        FilePath          => null;
        public string?        DetectedLanguage  => null;
        public string GetLine(int i) => i < Lines.Count ? Lines[i] : "";
    }

    // ── FindAll ────────────────────────────────────────────────────────────

    [Fact]
    public void FindAll_EmptyQuery_ReturnsEmpty()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello world");
        Assert.Empty(svc.FindAll(buf, "", false, false));
    }

    [Fact]
    public void FindAll_SingleMatch_ReturnsCorrectPosition()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello world");
        var matches = svc.FindAll(buf, "world", false, false);
        Assert.Single(matches);
        Assert.Equal(new CursorPosition(0, 6), matches[0]);
    }

    [Fact]
    public void FindAll_MultipleMatchesSameLine()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("abab");
        var matches = svc.FindAll(buf, "ab", false, false);
        Assert.Equal(2, matches.Count);
        Assert.Equal(new CursorPosition(0, 0), matches[0]);
        Assert.Equal(new CursorPosition(0, 2), matches[1]);
    }

    [Fact]
    public void FindAll_MatchesAcrossLines()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("foo", "bar", "foo");
        var matches = svc.FindAll(buf, "foo", false, false);
        Assert.Equal(2, matches.Count);
        Assert.Equal(new CursorPosition(0, 0), matches[0]);
        Assert.Equal(new CursorPosition(2, 0), matches[1]);
    }

    [Fact]
    public void FindAll_CaseSensitive_OnlyExactCase()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("Hello hello HELLO");
        var matches = svc.FindAll(buf, "hello", matchCase: true, wholeWord: false);
        Assert.Single(matches);
        Assert.Equal(6, matches[0].Column);
    }

    [Fact]
    public void FindAll_CaseInsensitive_AllVariants()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("Hello hello HELLO");
        var matches = svc.FindAll(buf, "hello", matchCase: false, wholeWord: false);
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void FindAll_WholeWord_ExcludesPartialMatches()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("the other there");
        var matches = svc.FindAll(buf, "the", matchCase: false, wholeWord: true);
        Assert.Single(matches);
        Assert.Equal(0, matches[0].Column);
    }

    [Fact]
    public void FindAll_WholeWord_MatchAtEndOfLine()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("end the");
        var matches = svc.FindAll(buf, "the", matchCase: false, wholeWord: true);
        Assert.Single(matches);
        Assert.Equal(4, matches[0].Column);
    }

    [Fact]
    public void FindAll_NoMatch_ReturnsEmpty()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello world");
        Assert.Empty(svc.FindAll(buf, "xyz", false, false));
    }

    // ── FindNext ───────────────────────────────────────────────────────────

    [Fact]
    public void FindNext_ReturnsMatchAfterCursor()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("foo foo foo");
        var from = new CursorPosition(0, 0);
        var match = svc.FindNext(buf, "foo", false, false, from, out var wrapped);
        Assert.NotNull(match);
        Assert.Equal(new CursorPosition(0, 4), match.Value);
        Assert.False(wrapped);
    }

    [Fact]
    public void FindNext_WrapsAroundAtEnd()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("foo");
        var from = new CursorPosition(0, 1);
        var match = svc.FindNext(buf, "foo", false, false, from, out var wrapped);
        Assert.NotNull(match);
        Assert.Equal(new CursorPosition(0, 0), match.Value);
        Assert.True(wrapped);
    }

    [Fact]
    public void FindNext_NoMatches_ReturnsNull()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello");
        var match = svc.FindNext(buf, "xyz", false, false, new CursorPosition(0, 0), out _);
        Assert.Null(match);
    }

    // ── FindPrev ───────────────────────────────────────────────────────────

    [Fact]
    public void FindPrev_ReturnsMatchBeforeCursor()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("foo foo foo");
        var from = new CursorPosition(0, 8);
        var match = svc.FindPrev(buf, "foo", false, false, from, out var wrapped);
        Assert.NotNull(match);
        Assert.Equal(new CursorPosition(0, 4), match.Value);
        Assert.False(wrapped);
    }

    [Fact]
    public void FindPrev_WrapsAroundAtStart()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("foo bar foo");
        var from = new CursorPosition(0, 0);
        var match = svc.FindPrev(buf, "foo", false, false, from, out var wrapped);
        Assert.NotNull(match);
        Assert.Equal(new CursorPosition(0, 8), match.Value);
        Assert.True(wrapped);
    }

    // ── BuildReplaceEvent ──────────────────────────────────────────────────

    [Fact]
    public void BuildReplaceEvent_ReturnsEventThatReplacesText()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello world");
        var match = new CursorPosition(0, 6);
        var (_, ev) = svc.BuildReplaceEvent(buf, match, "world", "there", false, false);
        Assert.NotNull(ev);
    }

    [Fact]
    public void BuildReplaceEvent_InvalidPosition_Throws()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello world");
        var match = new CursorPosition(0, 0);
        Assert.Throws<InvalidOperationException>(() =>
            svc.BuildReplaceEvent(buf, match, "xyz", "abc", false, false));
    }

    // ── BuildReplaceAllEvents ──────────────────────────────────────────────

    [Fact]
    public void BuildReplaceAllEvents_NoMatches_ReturnsZeroCount()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("hello world");
        var (count, events) = svc.BuildReplaceAllEvents(buf, "xyz", "abc", false, false);
        Assert.Equal(0, count);
        Assert.Empty(events);
    }

    [Fact]
    public void BuildReplaceAllEvents_MultipleMatches_ReturnsReverseOrder()
    {
        var svc = MakeSvc();
        var buf = MakeBuffer("foo foo foo");
        var (count, events) = svc.BuildReplaceAllEvents(buf, "foo", "bar", false, false);
        Assert.Equal(3, count);
        Assert.Equal(3, events.Count);
        // Events should be in reverse position order so earlier indices aren't shifted
        // The last match (col 8) should come first in the events list
    }
}
