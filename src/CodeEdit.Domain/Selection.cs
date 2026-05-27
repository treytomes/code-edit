namespace CodeEdit.Domain;

public readonly record struct Selection(CursorPosition Anchor, CursorPosition Active);
