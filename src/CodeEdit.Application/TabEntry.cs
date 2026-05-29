using CodeEdit.Domain;

namespace CodeEdit.Application;

public sealed class TabEntry
{
    public IMutableTextBuffer Buffer  { get; }
    public EventHistory       History { get; } = new();

    public TabEntry(IMutableTextBuffer buffer) => Buffer = buffer;
}
