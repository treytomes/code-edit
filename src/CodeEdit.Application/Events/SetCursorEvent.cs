using CodeEdit.Domain;

namespace CodeEdit.Application.Events;

public sealed class SetCursorEvent : IBufferEvent
{
    private readonly CursorPosition _to;
    private readonly CursorPosition _from;

    public SetCursorEvent(CursorPosition to, CursorPosition from)
    {
        _to   = to;
        _from = from;
    }

    public void Execute(IMutableTextBuffer buffer)
    {
        buffer.SetCursor(_to);
        buffer.SetSelection(null);
    }

    public void Undo(IMutableTextBuffer buffer)
    {
        buffer.SetCursor(_from);
        buffer.SetSelection(null);
    }

    public bool TryCoalesce(IBufferEvent next, out IBufferEvent merged)
    {
        merged = null!;
        if (next is not SetCursorEvent other) return false;
        merged = new SetCursorEvent(other._to, _from);
        return true;
    }

    public override string ToString() => $"{_from} → {_to}";
}
