namespace PacToolkits.Desktop.Tests;

/// <summary>可推进的 TimeProvider，驱动 Task.Delay(TimeProvider) 与 CreateTimer</summary>
internal sealed class ControllableTime : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _utc;
    private readonly List<Entry> _entries = [];

    public ControllableTime(DateTimeOffset utc)
        => _utc = utc;

    public ControllableTime()
        : this(DateTimeOffset.UtcNow)
    {
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utc;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var entry = new Entry(callback, state, period);
        lock (_gate)
        {
            entry.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _utc + dueTime;
            _entries.Add(entry);
        }

        return new TimerProxy(entry, this);
    }

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        List<(TimerCallback Callback, object? State)> fire;
        lock (_gate)
        {
            _utc += delta;
            fire = [];
            foreach (var entry in _entries)
            {
                while (!entry.Disposed && entry.Due is { } due && due <= _utc)
                {
                    fire.Add((entry.Callback, entry.State));
                    if (entry.Period > TimeSpan.Zero && entry.Period != Timeout.InfiniteTimeSpan)
                    {
                        entry.Due = due + entry.Period;
                    }
                    else
                    {
                        entry.Due = null;
                    }
                }
            }
        }

        foreach (var (callback, state) in fire)
        {
            callback(state);
        }
    }

    private void Change(Entry entry, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            entry.Period = period;
            entry.Due = dueTime == Timeout.InfiniteTimeSpan ? null : _utc + dueTime;
        }
    }

    private sealed class Entry(TimerCallback callback, object? state, TimeSpan period)
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public TimeSpan Period { get; set; } = period;
        public DateTimeOffset? Due { get; set; }
        public bool Disposed { get; set; }
    }

    private sealed class TimerProxy(Entry entry, ControllableTime owner) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (entry.Disposed)
            {
                return false;
            }

            owner.Change(entry, dueTime, period);
            return true;
        }

        public void Dispose()
            => entry.Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
