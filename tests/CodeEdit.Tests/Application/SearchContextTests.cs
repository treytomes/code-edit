using CodeEdit.Application;

namespace CodeEdit.Tests.Application;

public sealed class SearchContextTests
{
    private static FileMatches FM(string path, params int[] lines)
        => new(path, lines.Select(l => new LineMatch(l, $"line {l}", 0, 3)).ToList());

    [Fact]
    public void InitialKindIsNone()
        => Assert.Equal(SearchContextKind.None, new SearchContext().Kind);

    [Fact]
    public void SetInlineContext_SetsKindToInlineFind()
    {
        var ctx = new SearchContext();
        ctx.SetInlineContext();
        Assert.Equal(SearchContextKind.InlineFind, ctx.Kind);
    }

    [Fact]
    public void SetFindInFilesContext_SetsKindAndResetsIndices()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([FM("a.txt", 0, 1), FM("b.txt", 0)]);
        Assert.Equal(SearchContextKind.FindInFiles, ctx.Kind);
        Assert.Equal(0, ctx.FileIndex);
        Assert.Equal(0, ctx.MatchIndex);
    }

    [Fact]
    public void MoveNext_ReturnsNull_WhenKindIsNone()
        => Assert.Null(new SearchContext().MoveNext());

    [Fact]
    public void MoveNext_ReturnsNull_WhenResultsEmpty()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([]);
        Assert.Null(ctx.MoveNext());
    }

    [Fact]
    public void MoveNext_AdvancesThroughMatchesInFile()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([FM("a.txt", 0, 1, 2)]);
        var r1 = ctx.MoveNext();
        Assert.NotNull(r1);
        Assert.Equal(1, r1!.Value.match.LineNumber);
        Assert.False(r1.Value.wrapped);
    }

    [Fact]
    public void MoveNext_MovesToNextFile_WhenFileExhausted()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([FM("a.txt", 0), FM("b.txt", 5)]);
        var r = ctx.MoveNext();
        Assert.NotNull(r);
        Assert.Equal("b.txt", r!.Value.filePath);
        Assert.Equal(5, r.Value.match.LineNumber);
    }

    [Fact]
    public void MoveNext_WrapsToStart_WhenExhausted()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([FM("a.txt", 0)]);
        var r = ctx.MoveNext();
        Assert.NotNull(r);
        Assert.True(r!.Value.wrapped);
        Assert.Equal(0, ctx.FileIndex);
        Assert.Equal(0, ctx.MatchIndex);
    }

    [Fact]
    public void MovePrev_WrapsToEnd_WhenAtStart()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([FM("a.txt", 0), FM("b.txt", 5, 6)]);
        var r = ctx.MovePrev();
        Assert.NotNull(r);
        Assert.True(r!.Value.wrapped);
        Assert.Equal(1, ctx.FileIndex);
        Assert.Equal(1, ctx.MatchIndex);
    }

    [Fact]
    public void SetFindInFilesContext_WithNewResults_ResetsToZero()
    {
        var ctx = new SearchContext();
        ctx.SetFindInFilesContext([FM("a.txt", 0, 1)]);
        ctx.MoveNext();  // advance to index 1
        ctx.SetFindInFilesContext([FM("b.txt", 0)]);
        Assert.Equal(0, ctx.FileIndex);
        Assert.Equal(0, ctx.MatchIndex);
    }
}
