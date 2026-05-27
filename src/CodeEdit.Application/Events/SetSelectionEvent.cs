using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class SetSelectionEvent : IBufferEvent
{
    private readonly Selection?      _newSelection;
    private readonly CursorPosition  _newCursor;
    private readonly Selection?      _prevSelection;
    private readonly CursorPosition  _prevCursor;

    public SetSelectionEvent(
        Selection?     newSelection,
        CursorPosition newCursor,
        Selection?     prevSelection,
        CursorPosition prevCursor)
    {
        _newSelection  = newSelection;
        _newCursor     = newCursor;
        _prevSelection = prevSelection;
        _prevCursor    = prevCursor;
    }

    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.SetSelection(_newSelection);
        buffer.SetCursor(_newCursor);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.SetSelection(_prevSelection);
        buffer.SetCursor(_prevCursor);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        return false;
    }

    public override string ToString() => $"cursor={_newCursor} selection={(_newSelection.HasValue ? "set" : "null")}";
}
