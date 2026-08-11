using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Ui.Threading;

namespace PacToolkits.Desktop.Avalonia.Ui.State;

/// <summary>
/// 搜索框输入防抖，避免快速输入反复触发查询
/// </summary>
public sealed class SearchInputDebouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public SearchInputDebouncer(int delayMilliseconds)
        => _delay = TimeSpan.FromMilliseconds(Math.Max(0, delayMilliseconds));

    public void Schedule(Action action)
        => Schedule(() =>
        {
            action();
            return Task.CompletedTask;
        });

    public void Schedule(Func<Task> action)
    {
        CancellationTokenSource next;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            next = new CancellationTokenSource();
            _cts = next;
        }

        TaskObserve.Observe(
            RunDebouncedAsync(action, next),
            "SearchInputDebouncer",
            "search.debounce.fail",
            "Debounced search action failed");
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
        => Cancel();

    private async Task RunDebouncedAsync(Func<Task> action, CancellationTokenSource cts)
    {
        try
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cts.Token).ConfigureAwait(false);
            }

            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
