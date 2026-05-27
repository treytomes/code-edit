namespace CodeEdit.Domain;

public interface IBufferEvent
{
    void Execute(ITextBuffer buffer);
    void Undo(ITextBuffer buffer);
    bool TryCoalesce(IBufferEvent next, out IBufferEvent merged);
}
