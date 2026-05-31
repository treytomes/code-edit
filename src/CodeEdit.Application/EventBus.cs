using CodeEdit.Application.Events;
using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Application;

public sealed class EventBus : IEventBus
{
    private readonly ILogger<EventBus> _logger;

    public BufferManager Buffers { get; } = new();

    public ITextBuffer Buffer => Buffers.ActiveBuffer;

    public bool CanUndo => Buffers.ActiveHistory.CanUndo;
    public bool CanRedo => Buffers.ActiveHistory.CanRedo;

    public event EventHandler<BufferEventArgs>?        EventExecuted;
    public event EventHandler<BufferEventArgs>?        EventUndone;
    public event EventHandler<BufferEventArgs>?        EventRedone;
    public event EventHandler<BufferMutatedEventArgs>? BufferMutated;
    public event EventHandler?                         BufferChanged;

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
        Buffers.ActiveTabChanged += (_, _) => BufferChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Publish(IBufferEvent bufferEvent)
    {
        _logger.LogDebug("Publish {EventType}: {Description}", bufferEvent.GetType().Name, bufferEvent.ToString());

        bufferEvent.Execute(Buffers.ActiveBuffer);
        var stored = Buffers.ActiveHistory.Push(bufferEvent);
        EventExecuted?.Invoke(this, new BufferEventArgs(stored));
        FireMutated(stored);
    }

    public void Undo()
    {
        var ev = Buffers.ActiveHistory.TryUndo();
        if (ev is null)
        {
            _logger.LogWarning("Undo called with empty undo stack");
            return;
        }
        ev.Undo(Buffers.ActiveBuffer);
        EventUndone?.Invoke(this, new BufferEventArgs(ev));
        FireMutated(ev);
    }

    public void Redo()
    {
        var ev = Buffers.ActiveHistory.TryRedo();
        if (ev is null)
        {
            _logger.LogWarning("Redo called with empty redo stack");
            return;
        }
        ev.Execute(Buffers.ActiveBuffer);
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
