using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Persistence.Ingestion;

/// <summary>
/// Bounded LRU map from <see cref="InstrumentKey"/> to the surrogate
/// <c>InstrumentRecord.Id</c>. Lets <c>EfCoreMetricSink</c> resolve the FK
/// for incoming points without a SELECT per batch — the same role
/// <see cref="ResourceCache"/> plays for resource hashes.
/// </summary>
public sealed class InstrumentCache
{
    private readonly int _maxSize;
    private readonly LinkedList<InstrumentKey> _lru = new();
    private readonly Dictionary<InstrumentKey, Node> _lookup;
    private readonly Lock _gate = new();

    public InstrumentCache(int maxSize = 4_096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1);
        _maxSize = maxSize;
        _lookup = new Dictionary<InstrumentKey, Node>(_maxSize);
    }

    public bool TryGet(InstrumentKey key, out long id)
    {
        lock (_gate)
        {
            if (_lookup.TryGetValue(key, out var node))
            {
                _lru.Remove(node.LruNode);
                node.LruNode = _lru.AddFirst(key);
                _lookup[key] = node;
                id = node.Id;
                return true;
            }
            id = 0;
            return false;
        }
    }

    public void Set(InstrumentKey key, long id)
    {
        lock (_gate)
        {
            if (_lookup.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing.LruNode);
                existing.LruNode = _lru.AddFirst(key);
                existing.Id = id;
                _lookup[key] = existing;
                return;
            }

            if (_lookup.Count >= _maxSize)
            {
                var oldest = _lru.Last;
                if (oldest is not null)
                {
                    _lookup.Remove(oldest.Value);
                    _lru.RemoveLast();
                }
            }

            var lruNode = _lru.AddFirst(key);
            _lookup.Add(key, new Node(id, lruNode));
        }
    }

    public void Invalidate(InstrumentKey key)
    {
        lock (_gate)
        {
            if (_lookup.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing.LruNode);
                _lookup.Remove(key);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _lookup.Count;
            }
        }
    }

    // Mutable state per key: the surrogate Id and its position in the LRU
    // list. Wrapped so we can update the position without an extra dictionary
    // lookup on the hot path.
    private struct Node(long id, LinkedListNode<InstrumentKey> lruNode)
    {
        public long Id = id;
        public LinkedListNode<InstrumentKey> LruNode = lruNode;
    }
}
