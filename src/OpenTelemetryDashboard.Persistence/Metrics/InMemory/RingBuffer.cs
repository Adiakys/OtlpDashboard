namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

/// <summary>
/// Fixed-capacity circular buffer. Oldest items are overwritten when the buffer is full.
/// Thread-safe for multiple producers and consumers (coarse lock is adequate for the
/// expected per-series write rate; per-instrument contention is low).
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _gate = new();
    private int _writeIndex;
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get { lock (_gate) { return _count; } }
    }

    public void Write(T item)
    {
        lock (_gate)
        {
            _buffer[_writeIndex] = item;
            _writeIndex = (_writeIndex + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Returns a point-in-time snapshot ordered from oldest to newest.
    /// </summary>
    public T[] Snapshot()
    {
        lock (_gate)
        {
            return SnapshotLocked();
        }
    }

    /// <summary>
    /// Drops the leading (oldest) items for which <paramref name="predicate"/>
    /// returns <c>true</c>, stopping at the first item that does not match.
    /// Returns the number of items dropped. Items are evaluated in insertion
    /// order, so a monotonic predicate (e.g., "older than cutoff") is safe.
    /// </summary>
    public int RemoveWhile(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
        {
            if (_count == 0) return 0;

            var snapshot = SnapshotLocked();
            var skip = 0;
            while (skip < snapshot.Length && predicate(snapshot[skip]))
            {
                skip++;
            }

            if (skip == 0) return 0;

            Array.Clear(_buffer);
            _writeIndex = 0;
            _count = 0;
            for (var i = skip; i < snapshot.Length; i++)
            {
                _buffer[_writeIndex] = snapshot[i];
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                _count++;
            }
            return skip;
        }
    }

    private T[] SnapshotLocked()
    {
        if (_count == 0) return [];

        var snapshot = new T[_count];
        if (_count < _buffer.Length)
        {
            Array.Copy(_buffer, sourceIndex: 0, snapshot, destinationIndex: 0, _count);
        }
        else
        {
            // Buffer is full. _writeIndex points to the oldest slot.
            var tailLength = _buffer.Length - _writeIndex;
            Array.Copy(_buffer, _writeIndex, snapshot, 0, tailLength);
            Array.Copy(_buffer, 0, snapshot, tailLength, _writeIndex);
        }
        return snapshot;
    }
}
