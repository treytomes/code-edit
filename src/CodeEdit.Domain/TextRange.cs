namespace CodeEdit.Domain;

public readonly record struct TextRange(CursorPosition Start, CursorPosition End);
