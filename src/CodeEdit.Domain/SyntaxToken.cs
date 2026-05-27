namespace CodeEdit.Domain;

public readonly record struct SyntaxToken(int Line, int StartColumn, int Length, TokenType Type);
