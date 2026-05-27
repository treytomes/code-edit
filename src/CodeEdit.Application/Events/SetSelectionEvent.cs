using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class SetSelectionEvent(Selection? selection, CursorPosition cursorTo) : IBufferEvent
{
    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.SetSelection(selection);
        buffer.SetCursor(cursorTo);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }

    public override string ToString() => $"cursor={cursorTo} selection={(selection.HasValue ? "set" : "null")}";
}
