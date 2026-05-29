using CodeEdit.Application.Events;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Application;

public sealed class EventBus(ILogger<EventBus> logger) : IEventBus
{
    private readonly ILogger<EventBus> _logger = logger;
    private IMutableTextBuffer? _buffer;
    private readonly EventHistory _history = new();

    public ITextBuffer Buffer =>
        _buffer ?? throw new InvalidOperationException("Buffer has not been set. Call SetBuffer before using the event bus.");

    public void SetBuffer(IMutableTextBuffer buffer)
    {
        _buffer = buffer;
        _history.Clear();
    }

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public event EventHandler<BufferEventArgs>? EventExecuted;
    public event EventHandler<BufferEventArgs>? EventUndone;
    public event EventHandler<BufferEventArgs>? EventRedone;
    public event EventHandler<BufferMutatedEventArgs>? BufferMutated;

    public void Publish(IBufferEvent bufferEvent)
    {
        var buf = _buffer ?? throw new InvalidOperationException("Buffer has not been set.");

        _logger.LogDebug("Publish {EventType}: {Description}", bufferEvent.GetType().Name, bufferEvent.ToString());

        bufferEvent.Execute(buf);
        var stored = _history.Push(bufferEvent);
        EventExecuted?.Invoke(this, new BufferEventArgs(stored));
        FireMutated(stored);
    }

    public void Undo()
    {
        var ev = _history.TryUndo();
        if (ev is null)
        {
            _logger.LogWarning("Undo called with empty undo stack");
            return;
        }
        var buf = _buffer ?? throw new InvalidOperationException("Buffer has not been set.");
        ev.Undo(buf);
        EventUndone?.Invoke(this, new BufferEventArgs(ev));
        FireMutated(ev);
    }

    public void Redo()
    {
        var ev = _history.TryRedo();
        if (ev is null)
        {
            _logger.LogWarning("Redo called with empty redo stack");
            return;
        }
        var buf = _buffer ?? throw new InvalidOperationException("Buffer has not been set.");
        ev.Execute(buf);
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
