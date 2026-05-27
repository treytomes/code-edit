using CodeEdit.Domain;
using Microsoft.Extensions.Logging;

namespace CodeEdit.Application;

// Implemented per the event-bus feature spec.
public sealed class EventBus(ILogger<EventBus> logger) : IEventBus
{
    private readonly ILogger<EventBus> _logger = logger;

    public bool CanUndo => false;
    public bool CanRedo => false;

    public event EventHandler<BufferEventArgs>? EventExecuted;
    public event EventHandler<BufferEventArgs>? EventUndone;
    public event EventHandler<BufferEventArgs>? EventRedone;

    public void Publish(IBufferEvent bufferEvent) => throw new NotImplementedException();
    public void Undo() => throw new NotImplementedException();
    public void Redo() => throw new NotImplementedException();
}
