namespace CodeEdit.Domain;

public interface IBufferEvent
{
    void Execute(IMutableTextBuffer buffer);
    void Undo(IMutableTextBuffer buffer);
    bool TryCoalesce(IBufferEvent next, out IBufferEvent merged);
}
