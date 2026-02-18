using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.Behaviors;

public sealed class PageReloadBehavior : IDisposable
{
    private CancellationTokenSource? _cts;

    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(300);

    public async Task RunAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        _cts?.Cancel();
        _cts?.Dispose();

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
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
