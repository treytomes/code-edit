namespace CodeEdit.Application;

public sealed record FileMatches(
    string                  FilePath,
    IReadOnlyList<LineMatch> Matches);
