namespace CodeEdit.Domain;

public readonly record struct LineTokens(IReadOnlyList<SyntaxToken> Tokens, int EndState);
