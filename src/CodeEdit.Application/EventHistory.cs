using CodeEdit.Domain;

namespace CodeEdit.Application;

public sealed class EventHistory
{
    private readonly Stack<IBufferEvent> _undoStack = new();
    private readonly Stack<IBufferEvent> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    // Returns the event actually stored — either the original or the coalesced replacement.
    public IBufferEvent Push(IBufferEvent ev)
    {
        if (_undoStack.Count > 0 && _undoStack.Peek().TryCoalesce(ev, out var merged))
        {
            _undoStack.Pop();
            _undoStack.Push(merged);
            _redoStack.Clear();
            return merged;
        }
        _undoStack.Push(ev);
        _redoStack.Clear();
        return ev;
    }

    public IBufferEvent? TryUndo()
    {
        if (_undoStack.Count == 0) return null;
        var ev = _undoStack.Pop();
        _redoStack.Push(ev);
        return ev;
    }

    public IBufferEvent? TryRedo()
    {
        if (_redoStack.Count == 0) return null;
        var ev = _redoStack.Pop();
        _undoStack.Push(ev);
        return ev;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
