using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PacToolkits.Api.Changes;

/// <summary>
/// 进程内变更分发：LISTEN 通知写入各 SSE 订阅通道
///
/// 每 client_id 并发订阅有上限；突发合并在 <see cref="PostgresNotifyListener"/>
/// </summary>
public sealed class ChangeBus
{
    public const int MaxSubscriptionsPerClient = 2;

    private readonly ConcurrentDictionary<Guid, Subscription> _subs = new();
    private readonly ConcurrentDictionary<string, int> _perClient = new(StringComparer.Ordinal);
    private readonly ILogger<ChangeBus> _logger;

    public ChangeBus(ILogger<ChangeBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TrySubscribe(string clientId, out ChangeSubscription subscription)
    {
        subscription = null!;
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var id = clientId.Trim();
        var count = _perClient.AddOrUpdate(id, 1, (_, n) => n + 1);
        if (count > MaxSubscriptionsPerClient)
        {
            _perClient.AddOrUpdate(id, 0, (_, n) => Math.Max(0, n - 1));
            return false;
        }

        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            // 订阅落后时丢旧 topic；version 以 GET watermarks 为准
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var sub = new Subscription(Guid.NewGuid(), id, channel);
        _subs[sub.Id] = sub;
        subscription = new ChangeSubscription(channel.Reader, () => Unsubscribe(sub.Id));
        return true;
    }

    public void Publish(string topic)
    {
        var t = string.IsNullOrWhiteSpace(topic) ? "inventory" : topic.Trim();
        foreach (var sub in _subs.Values)
        {
            sub.Channel.Writer.TryWrite(t);
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (!_subs.TryRemove(id, out var sub))
        {
            return;
        }

        sub.Channel.Writer.TryComplete();
        _perClient.AddOrUpdate(sub.ClientId, 0, (_, n) => Math.Max(0, n - 1));
        _logger.LogDebug("change.unsubscribed clientId={ClientId} subId={SubId}", sub.ClientId, id);
    }

    private sealed class Subscription(Guid id, string clientId, Channel<string> channel)
    {
        public Guid Id { get; } = id;
        public string ClientId { get; } = clientId;
        public Channel<string> Channel { get; } = channel;
    }
}

/// <summary>SSE 单路订阅（有界 channel；Dispose 时退订）</summary>
public sealed class ChangeSubscription : IAsyncDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    public ChangeSubscription(ChannelReader<string> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<string> Reader { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _dispose();
        }

        return ValueTask.CompletedTask;
    }
}
