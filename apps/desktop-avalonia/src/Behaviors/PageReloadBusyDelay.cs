using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

internal static class PageReloadBusyDelay
{
    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(300);

    public static async Task RunAsync(
        CancellationToken ct,
        Action<bool> setBusy,
        Func<Task> body)
    {
        using var busyDelayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var busyDelayTask = Task.Delay(BusyDelay, busyDelayCts.Token);
        var bodyTask = body();

        var first = await Task.WhenAny(bodyTask, busyDelayTask).ConfigureAwait(false);
        var busyShown = false;

        if (first == busyDelayTask && !bodyTask.IsCompleted && !busyDelayCts.IsCancellationRequested)
        {
            busyShown = true;
            await UiThreadHelper.RunOnUiAsync(() => setBusy(true)).ConfigureAwait(false);
        }

        try
        {
            await bodyTask.ConfigureAwait(false);
        }
        finally
        {
            busyDelayCts.Cancel();
            try
            {
                await busyDelayTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (busyShown)
            {
                await UiThreadHelper.RunOnUiAsync(() => setBusy(false)).ConfigureAwait(false);
            }
        }
    }
}
