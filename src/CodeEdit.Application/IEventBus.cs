using CodeEdit.Application.Events;
using CodeEdit.Domain;

namespace CodeEdit.Application;

public class BufferEventArgs(IBufferEvent bufferEvent) : EventArgs
{
    public IBufferEvent BufferEvent { get; } = bufferEvent;
}

public interface IEventBus
{
    BufferManager Buffers { get; }

    // Convenience: forwards to Buffers.ActiveBuffer
    ITextBuffer Buffer { get; }

    void Publish(IBufferEvent bufferEvent);
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    event EventHandler<BufferEventArgs>      EventExecuted;
    event EventHandler<BufferEventArgs>      EventUndone;
    event EventHandler<BufferEventArgs>      EventRedone;
    event EventHandler<BufferMutatedEventArgs> BufferMutated;

    // Fired when the active tab changes (Buffers.ActiveTabChanged forwarded)
    event EventHandler? BufferChanged;
}
