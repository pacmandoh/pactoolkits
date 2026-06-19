using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// Debounces search-box input so rapid typing does not trigger repeated queries.
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

        _ = RunDebouncedAsync(action, next);
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
