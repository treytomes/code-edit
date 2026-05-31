using CodeEdit.Domain;

namespace CodeEdit.Application;

public sealed class BufferManager
{
    private readonly List<TabEntry> _tabs = [];

    public IReadOnlyList<TabEntry> Tabs        => _tabs;
    public int                     ActiveIndex { get; private set; } = 0;

    public IMutableTextBuffer ActiveBuffer => _tabs[ActiveIndex].Buffer;
    public EventHistory        ActiveHistory => _tabs[ActiveIndex].History;

    public event EventHandler<int>? ActiveTabChanged;
    public event EventHandler<int>? TabClosed;
    public event EventHandler?      TabsChanged;

    public void Add(IMutableTextBuffer buffer)
    {
        // De-duplicate by FilePath (non-null only — multiple Untitled tabs are allowed)
        if (buffer.FilePath is not null)
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Buffer.FilePath, buffer.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Activate(i);
                    return;
                }
            }
        }

        _tabs.Add(new TabEntry(buffer));
        ActiveIndex = _tabs.Count - 1;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        ActiveTabChanged?.Invoke(this, ActiveIndex);
    }

    public void Activate(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == ActiveIndex) return;

        ActiveIndex = index;
        ActiveTabChanged?.Invoke(this, ActiveIndex);
    }

    public void Close(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (_tabs.Count == 1)
            throw new InvalidOperationException("Cannot close the last tab. Add a replacement buffer first.");

        _tabs.RemoveAt(index);

        if (ActiveIndex >= _tabs.Count)
            ActiveIndex = _tabs.Count - 1;
        else if (index < ActiveIndex)
            ActiveIndex--;

        TabClosed?.Invoke(this, index);
        TabsChanged?.Invoke(this, EventArgs.Empty);
        ActiveTabChanged?.Invoke(this, ActiveIndex);
    }
}
