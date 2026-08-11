using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Diagnostics;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 保证同一页面最多执行一个有效重载任务；加载状态由调用方管理
/// </summary>
public sealed class PageReload : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _runId;
    private bool _disposed;

    public bool IsActive => Volatile.Read(ref _cts) is not null;

    public async Task RunAsync(
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PageReload));
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);
        CancelCts(previous);

        var runId = Interlocked.Increment(ref _runId);
        var ct = cts.Token;

        Exception? error = null;

        try
        {
            await action(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            error = ex;
            AppLog.Warn("PageReload", "reload.run.fail", "Page reload behavior captured exception", ex);
        }
        finally
        {
            // 已被替换的任务不得更新界面，以免覆盖当前页面状态
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

    public void CancelActiveRun()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null)
        {
            return;
        }

        CancelCts(cts);
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
        }
    }
}
