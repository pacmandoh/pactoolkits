using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

public sealed class PageReloadBehavior : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _runId;
    private bool _disposed;

    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(300);

    public async Task RunAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PageReloadBehavior));
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);
        CancelCts(previous);

        var runId = Interlocked.Increment(ref _runId);
        var ct = cts.Token;

        var busyShown = false;
        Exception? error = null;

        try
        {
            var actionTask = action(ct);
            var delayTask = Task.Delay(BusyDelay, ct);

            var first = await Task.WhenAny(actionTask, delayTask).ConfigureAwait(false);

            if (first == delayTask && !actionTask.IsCompleted && !ct.IsCancellationRequested && IsCurrentRun(runId, cts))
            {
                busyShown = true;
                await Dispatcher.UIThread.InvokeAsync(() => setBusy(true));
            }

            await actionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            error = ex;
            AppLog.Warn("PageReloadBehavior", "reload.run.fail", "Page reload behavior captured exception", ex);
        }
        finally
        {
            if (busyShown && IsCurrentRun(runId, cts))
            {
                await Dispatcher.UIThread.InvokeAsync(() => setBusy(false));
            }

            if (onFinished is not null && IsCurrentRun(runId, cts))
            {
                await Dispatcher.UIThread.InvokeAsync(onFinished);
            }

            Interlocked.CompareExchange(ref _cts, null, cts);
            cts.Dispose();
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelActiveRun();
    }

    private bool IsCurrentRun(int runId, CancellationTokenSource cts)
        => Volatile.Read(ref _runId) == runId && ReferenceEquals(Volatile.Read(ref _cts), cts);

    private void CancelActiveRun()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null)
        {
            return;
        }

        CancelCts(cts);
    }

    private static void CancelCts(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore races: the owning reload disposes its CTS when it exits.
        }
    }
}
