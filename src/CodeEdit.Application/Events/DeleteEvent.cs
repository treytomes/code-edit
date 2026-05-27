using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class DeleteEvent(TextRange range, string deletedText) : IBufferEvent
{
    public TextRange Range       { get; } = range;
    public string    DeletedText { get; } = deletedText;

    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.DeleteRange(Range);
        buffer.SetCursor(Range.Start);
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.InsertText(Range.Start, DeletedText);
        buffer.SetCursor(Range.End);
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }

    public override string ToString() => $"range={Range} text={DeletedText.Length}ch";
}
