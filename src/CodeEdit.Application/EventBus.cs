using CodeEdit.Application.Events;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Application;

public sealed class EventBus(ILogger<EventBus> logger) : IEventBus
{
    private readonly ILogger<EventBus> _logger = logger;
    private IMutableTextBuffer? _buffer;
    private readonly Stack<IBufferEvent> _undoStack = new();
    private readonly Stack<IBufferEvent> _redoStack = new();

    public ITextBuffer Buffer =>
        _buffer ?? throw new InvalidOperationException("Buffer has not been set. Call SetBuffer before using the event bus.");

    public void SetBuffer(IMutableTextBuffer buffer)
    {
        _buffer = buffer;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler<BufferEventArgs>? EventExecuted;
    public event EventHandler<BufferEventArgs>? EventUndone;
    public event EventHandler<BufferEventArgs>? EventRedone;
    public event EventHandler<BufferMutatedEventArgs>? BufferMutated;

    public void Publish(IBufferEvent bufferEvent)
    {
        var buf = _buffer ?? throw new InvalidOperationException("Buffer has not been set.");

        _logger.LogDebug("Publish {EventType}: {Description}", bufferEvent.GetType().Name, bufferEvent.ToString());

        bufferEvent.Execute(buf);

        if (_undoStack.Count > 0)
        {
            var top = _undoStack.Peek();
            if (top.TryCoalesce(bufferEvent, out var merged))
            {
                _undoStack.Pop();
                _undoStack.Push(merged);
                _redoStack.Clear();
                EventExecuted?.Invoke(this, new BufferEventArgs(merged));
                FireMutated(merged);
                return;
            }
        }

        _undoStack.Push(bufferEvent);
        _redoStack.Clear();
        EventExecuted?.Invoke(this, new BufferEventArgs(bufferEvent));
        FireMutated(bufferEvent);
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            _logger.LogWarning("Undo called with empty undo stack");
            return;
        }

        var buf = _buffer ?? throw new InvalidOperationException("Buffer has not been set.");
        var ev = _undoStack.Pop();
        ev.Undo(buf);
        _redoStack.Push(ev);
        EventUndone?.Invoke(this, new BufferEventArgs(ev));
        FireMutated(ev);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            _logger.LogWarning("Redo called with empty redo stack");
            return;
        }

        var buf = _buffer ?? throw new InvalidOperationException("Buffer has not been set.");
        var ev = _redoStack.Pop();
        ev.Execute(buf);
        _undoStack.Push(ev);
        EventRedone?.Invoke(this, new BufferEventArgs(ev));
        FireMutated(ev);
    }

    private void FireMutated(IBufferEvent ev)
    {
        var line = ev switch
        {
            InsertTextEvent e => e.At.Line,
            DeleteEvent     e => e.Range.Start.Line,
            CutEvent        e => e.Range.Start.Line,
            PasteEvent      e => e.At.Line,
            IndentEvent     e => e.Lines.Count > 0 ? e.Lines[0] : -1,
            _                 => -1
        };
        if (line >= 0)
            BufferMutated?.Invoke(this, new BufferMutatedEventArgs(line));
    }
}
