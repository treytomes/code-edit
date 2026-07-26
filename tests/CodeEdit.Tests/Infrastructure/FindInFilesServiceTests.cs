using CodeEdit.Application;
using CodeEdit.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeEdit.Tests.Infrastructure;

public sealed class FindInFilesServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private FindInFilesService Svc() => new(NullLogger<FindInFilesService>.Instance);

    public FindInFilesServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private void Write(string relPath, string content)
    {
        var full = Path.Combine(_dir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private IReadOnlyList<FileMatches> Search(string query, FindInFilesOptions? opts = null)
        => Svc().Search(_dir, query, opts ?? new FindInFilesOptions());

    [Fact]
    public void EmptyQuery_ReturnsEmptyList()
        => Assert.Empty(Search(""));

    [Fact]
    public void NoMatches_ReturnsEmptyList()
    {
        Write("a.txt", "hello world");
        Assert.Empty(Search("zzz"));
    }

    [Fact]
    public void MatchesInMultipleFiles_ReturnsCorrectLineNumberAndColumn()
    {
        Write("a.txt", "foo\nbar\nfoo");
        Write("b.txt", "not here\nfoo here");
        var results = Search("foo");
        var navigable = results.Where(f => !string.IsNullOrEmpty(f.FilePath)).ToList();
        Assert.Equal(2, navigable.Count);
        Assert.Equal(2, navigable.First(f => f.FilePath.EndsWith("a.txt")).Matches.Count);
        Assert.Equal(1, navigable.First(f => f.FilePath.EndsWith("b.txt")).Matches.Count);
        var firstMatch = navigable.First(f => f.FilePath.EndsWith("b.txt")).Matches[0];
        Assert.Equal(1, firstMatch.LineNumber);
        Assert.Equal(0, firstMatch.MatchColumn);
        Assert.Equal(3, firstMatch.MatchLength);
    }

    [Fact]
    public void CaseInsensitive_MatchesVariants()
    {
        Write("a.txt", "Hello HELLO hello");
        var results = Search("hello", new FindInFilesOptions(CaseSensitive: false));
        Assert.Equal(3, results[0].Matches.Count);
    }

    [Fact]
    public void CaseSensitive_DoesNotMatchOtherCase()
    {
        Write("a.txt", "Hello HELLO hello");
        var results = Search("hello", new FindInFilesOptions(CaseSensitive: true));
        Assert.Single(results[0].Matches);
    }

    [Fact]
    public void WholeWord_DoesNotMatchSubstring()
    {
        Write("a.txt", "he hello helloWorld");
        var results = Search("hello", new FindInFilesOptions(WholeWord: true));
        Assert.Single(results[0].Matches);
        Assert.Equal(3, results[0].Matches[0].MatchColumn);
    }

    [Fact]
    public void WholeWord_MatchesWhenSurroundedByNonWordChars()
    {
        Write("a.txt", "say 'hello' to the world");
        var results = Search("hello", new FindInFilesOptions(WholeWord: true));
        Assert.Single(results[0].Matches);
    }

    [Fact]
    public void BinaryFile_IsSkipped()
    {
        var path = Path.Combine(_dir, "bin.dat");
        File.WriteAllBytes(path, new byte[] { 0x68, 0x65, 0x00, 0x6C, 0x6F });  // "he\0lo"
        Assert.Empty(Search("he"));
    }

    [Fact]
    public void SkippedDirs_AreNotSearched()
    {
        Write("bin/hidden.cs",  "findme");
        Write("obj/hidden.cs",  "findme");
        Write(".git/config",    "findme");
        Write("src/visible.cs", "findme");
        var results = Search("findme").Where(f => !string.IsNullOrEmpty(f.FilePath)).ToList();
        Assert.Single(results);
        Assert.Contains("visible.cs", results[0].FilePath);
    }

    [Fact]
    public void FileGlob_FiltersExtension()
    {
        Write("a.cs",  "findme");
        Write("b.txt", "findme");
        var results = Search("findme", new FindInFilesOptions(FileGlob: "*.cs"))
                        .Where(f => !string.IsNullOrEmpty(f.FilePath)).ToList();
        Assert.Single(results);
        Assert.EndsWith(".cs", results[0].FilePath);
    }

    [Fact]
    public void EmptyGlob_ReturnsAllFiles()
    {
        Write("a.cs",  "findme");
        Write("b.txt", "findme");
        var results = Search("findme").Where(f => !string.IsNullOrEmpty(f.FilePath)).ToList();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void MultipleMatchesOnSameLine_AllRecorded()
    {
        Write("a.txt", "foo foo foo");
        var results = Search("foo");
        Assert.Equal(3, results[0].Matches.Count);
    }

    [Fact]
    public void UnreadableFile_IsSkipped_OtherFilesStillReturned()
    {
        // Simulate by giving an unreadable path by putting a subdir where a file should go
        Directory.CreateDirectory(Path.Combine(_dir, "fake.txt"));
        Write("real.txt", "findme");
        var results = Search("findme").Where(f => !string.IsNullOrEmpty(f.FilePath)).ToList();
        Assert.Single(results);
    }

    [Fact]
    public void UseRegex_ValidPattern_MatchesCorrectly()
    {
        Write("a.txt", "helo hello helllo");
        var results = Search("hel+o", new FindInFilesOptions(UseRegex: true));
        Assert.Equal(3, results[0].Matches.Count);
    }

    [Fact]
    public void UseRegex_InvalidPattern_ThrowsArgumentException()
    {
        Write("a.txt", "test");
        Assert.ThrowsAny<ArgumentException>(() =>
            Search("[unclosed", new FindInFilesOptions(UseRegex: true)));
    }

    [Fact]
    public void UseRegex_IgnoresWholeWordOption()
    {
        Write("a.txt", "helloWorld");
        // WholeWord should be ignored when regex is on
        var results = Search("hello", new FindInFilesOptions(UseRegex: true, WholeWord: true));
        Assert.Single(results[0].Matches);
    }

    [Fact]
    public void ResultCap_Appendssentinel_WhenTruncated()
    {
        // Create a file with 1001 matches
        Write("a.txt", string.Join("\n", Enumerable.Repeat("x", 1001)));
        var results = Search("x");
        var sentinel = results.FirstOrDefault(f => string.IsNullOrEmpty(f.FilePath));
        Assert.NotNull(sentinel);
        Assert.Contains("truncated", sentinel!.Matches[0].LineText, StringComparison.OrdinalIgnoreCase);
    }
}
