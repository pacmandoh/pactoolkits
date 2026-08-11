namespace PacToolkits.Application.Threading;

/// <summary>
/// 用 TimeProvider 定时取消 CTS
///
/// 异步释放 timer，并在回调里带关闭门闩，避免 Dispose 后迟到的 Cancel 打到已释放的 CTS
/// </summary>
public sealed class CancelAfter : IAsyncDisposable, IDisposable
{
    private readonly ITimer? _timer;
    private readonly Gate _gate;
    private int _disposed;

    private CancelAfter(ITimer? timer, Gate gate)
    {
        _timer = timer;
        _gate = gate;
    }

    public static CancelAfter Schedule(CancellationTokenSource cts, TimeSpan delay, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(cts);
        ArgumentNullException.ThrowIfNull(time);

        var gate = new Gate(cts);
        if (delay <= TimeSpan.Zero)
        {
            gate.TryCancel();
            return new CancelAfter(timer: null, gate);
        }

        var timer = time.CreateTimer(
            static state => ((Gate)state!).TryCancel(),
            gate,
            delay,
            Timeout.InfiniteTimeSpan);
        return new CancelAfter(timer, gate);
    }

    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 先关门：迟到回调不再碰 CTS；再等 timer 排空
        _gate.Close();
        if (_timer is not null)
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class Gate(CancellationTokenSource cts)
    {
        // 0=可取消；1=已关闭（正在/已经 Dispose）
        private int _closed;

        public void TryCancel()
        {
            if (Interlocked.CompareExchange(ref _closed, 1, 0) != 0)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Close()
            => Interlocked.Exchange(ref _closed, 1);
    }
}
