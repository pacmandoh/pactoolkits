using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

public sealed class PageReloadBehavior : IDisposable
{
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(300);

    public async Task RunAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PageReloadBehavior));

        CancelAndDisposeCts();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var busyShown = false;
        Exception? error = null;

        try
        {
            var actionTask = action(ct);
            var delayTask = Task.Delay(BusyDelay, ct);

            var first = await Task.WhenAny(actionTask, delayTask).ConfigureAwait(false);

            if (first == delayTask && !actionTask.IsCompleted && !ct.IsCancellationRequested)
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
            if (busyShown)
                await Dispatcher.UIThread.InvokeAsync(() => setBusy(false));

            if (onFinished is not null)
                await Dispatcher.UIThread.InvokeAsync(onFinished);
        }

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAndDisposeCts();
    }

    private void CancelAndDisposeCts()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore races: CTS may already be disposed by concurrent path.
        }
        finally
        {
            cts.Dispose();
        }
    }
}
