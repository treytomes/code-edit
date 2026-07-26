namespace CodeEdit.Application;

public enum SearchContextKind { None, InlineFind, FindInFiles }

public sealed class SearchContext
{
    public SearchContextKind          Kind               { get; private set; } = SearchContextKind.None;
    public IReadOnlyList<FileMatches> FindInFilesResults { get; private set; } = [];
    public int                        FileIndex          { get; private set; } = 0;
    public int                        MatchIndex         { get; private set; } = 0;

    public void SetInlineContext()
    {
        Kind = SearchContextKind.InlineFind;
    }

    public void SetFindInFilesContext(IReadOnlyList<FileMatches> results)
    {
        Kind               = SearchContextKind.FindInFiles;
        FindInFilesResults = results;
        FileIndex          = 0;
        MatchIndex         = 0;
    }

    public (string filePath, LineMatch match, bool wrapped)? MoveNext()
    {
        var results = NavigableResults();
        if (results.Count == 0) return null;

        var fileMatches = results[FileIndex];
        var nextMatch   = MatchIndex + 1;

        if (nextMatch < fileMatches.Matches.Count)
        {
            MatchIndex = nextMatch;
            return (fileMatches.FilePath, fileMatches.Matches[MatchIndex], false);
        }

        var nextFile = FileIndex + 1;
        if (nextFile < results.Count)
        {
            FileIndex  = nextFile;
            MatchIndex = 0;
            return (results[FileIndex].FilePath, results[FileIndex].Matches[0], false);
        }

        // Wrap to start
        FileIndex  = 0;
        MatchIndex = 0;
        return (results[0].FilePath, results[0].Matches[0], wrapped: true);
    }

    public (string filePath, LineMatch match, bool wrapped)? MovePrev()
    {
        var results = NavigableResults();
        if (results.Count == 0) return null;

        if (MatchIndex > 0)
        {
            MatchIndex--;
            return (results[FileIndex].FilePath, results[FileIndex].Matches[MatchIndex], false);
        }

        if (FileIndex > 0)
        {
            FileIndex--;
            MatchIndex = results[FileIndex].Matches.Count - 1;
            return (results[FileIndex].FilePath, results[FileIndex].Matches[MatchIndex], false);
        }

        // Wrap to end
        FileIndex  = results.Count - 1;
        MatchIndex = results[FileIndex].Matches.Count - 1;
        return (results[FileIndex].FilePath, results[FileIndex].Matches[MatchIndex], wrapped: true);
    }

    // Excludes the truncation-sentinel entry (empty FilePath) from navigation.
    private IReadOnlyList<FileMatches> NavigableResults()
        => FindInFilesResults
            .Where(f => !string.IsNullOrEmpty(f.FilePath))
            .ToList();
}
