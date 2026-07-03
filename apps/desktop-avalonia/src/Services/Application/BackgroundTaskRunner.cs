using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed class BackgroundTaskRunner(IAppLogger logger, IToastService toast) : IBackgroundTaskRunner
{
    public void RunDetached(
        Func<CancellationToken, Task> work,
        string module,
        string eventName,
        CancellationToken ct = default,
        bool toastOnError = false)
    {
        TaskObserve.Observe(RunDetachedAsync(work, module, eventName, ct, toastOnError), module, eventName);
    }

    private async Task RunDetachedAsync(
        Func<CancellationToken, Task> work,
        string module,
        string eventName,
        CancellationToken ct,
        bool toastOnError)
    {
        try
        {
            await work(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.Warn(module, eventName, "Background task failed", ex);
            if (!toastOnError)
            {
                return;
            }

            try
            {
                await UiThreadHelper.RunOnUiAsync(() =>
                    toast.Error("操作失败", ex.Message)).ConfigureAwait(false);
            }
            catch (Exception toastEx)
            {
                logger.Warn(module, $"{eventName}.toast_fail", "Failed to show background task error toast", toastEx);
            }
        }
    }
}
