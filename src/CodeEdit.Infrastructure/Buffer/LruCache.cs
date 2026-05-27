namespace CodeEdit.Infrastructure.Buffer;

internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _list = new();
    private int _capacity;

    internal LruCache(int capacity)
    {
        _capacity = capacity < 1 ? 1 : capacity;
    }

    internal bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    internal void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _list.Remove(existing);
            _map.Remove(key);
        }

        var node = new LinkedListNode<(TKey, TValue)>((key, value));
        _list.AddFirst(node);
        _map[key] = node;

        while (_list.Count > _capacity)
        {
            var tail = _list.Last!;
            _map.Remove(tail.Value.Key);
            _list.RemoveLast();
        }
    }

    internal void Invalidate(TKey key)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _map.Remove(key);
        }
    }

    internal void Resize(int newCapacity)
    {
        _capacity = newCapacity < 1 ? 1 : newCapacity;
        while (_list.Count > _capacity)
        {
            var tail = _list.Last!;
            _map.Remove(tail.Value.Key);
            _list.RemoveLast();
        }
    }

    internal void Clear()
    {
        _map.Clear();
        _list.Clear();
    }

    internal int Count => _list.Count;
}
