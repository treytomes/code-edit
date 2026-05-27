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
}
