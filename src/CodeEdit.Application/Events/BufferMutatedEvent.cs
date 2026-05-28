namespace CodeEdit.Application.Events;

public sealed class BufferMutatedEventArgs(int firstAffectedLine) : EventArgs
{
    public int FirstAffectedLine { get; } = firstAffectedLine;
}
