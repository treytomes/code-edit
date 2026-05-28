using CodeEdit.Application.Events;
using CodeEdit.Domain;

namespace CodeEdit.Application;

public class BufferEventArgs(IBufferEvent bufferEvent) : EventArgs
{
    public IBufferEvent BufferEvent { get; } = bufferEvent;
}

public interface IEventBus
{
    ITextBuffer Buffer { get; }
    void SetBuffer(IMutableTextBuffer buffer);
    void Publish(IBufferEvent bufferEvent);
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    event EventHandler<BufferEventArgs> EventExecuted;
    event EventHandler<BufferEventArgs> EventUndone;
    event EventHandler<BufferEventArgs> EventRedone;

    /// <summary>
    /// Fired after any mutating event executes or is undone/redone,
    /// with the index of the first logical line affected.
    /// </summary>
    event EventHandler<BufferMutatedEventArgs> BufferMutated;
}
