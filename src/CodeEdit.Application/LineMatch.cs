namespace CodeEdit.Application;

public sealed record LineMatch(
    int    LineNumber,    // 0-based
    string LineText,
    int    MatchColumn,   // 0-based column of match start
    int    MatchLength);
