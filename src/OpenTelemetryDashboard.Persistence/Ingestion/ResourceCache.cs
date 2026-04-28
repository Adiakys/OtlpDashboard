using OpenTelemetryDashboard.Core.Common;

namespace OpenTelemetryDashboard.Persistence.Ingestion;

/// <summary>
/// Bounded LRU set of resource hashes already known to exist in the database.
/// Prevents a database round-trip for every Resource in every batch, since
/// clients emit the same resource attributes repeatedly.
/// </summary>
public sealed class ResourceCache
{
    private readonly int _maxSize;
    private readonly LinkedList<byte[]> _lru = new();
    private readonly Dictionary<byte[], LinkedListNode<byte[]>> _lookup;
    private readonly Lock _gate = new();

    public ResourceCache(int maxSize = 1_024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1);
        _maxSize = maxSize;
        _lookup = new Dictionary<byte[], LinkedListNode<byte[]>>(ByteArrayEqualityComparer.Instance);
    }

    public bool Contains(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        lock (_gate)
        {
            if (_lookup.TryGetValue(hash, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return true;
            }
            return false;
        }
    }

    public void Add(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        lock (_gate)
        {
            if (_lookup.TryGetValue(hash, out var existing))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
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

            var node = _lru.AddFirst(hash);
            _lookup.Add(hash, node);
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
}
