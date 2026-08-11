using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.Tests;

public sealed class PageReloadTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Superseding_run_cancels_previous_action()
    {
        var gate = new PageReload();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = gate.RunAsync(async ct =>
        {
            firstStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                firstCancelled.SetResult();
                throw;
            }
        });

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestCt);
        Assert.True(gate.IsActive);

        var secondCompleted = false;
        var second = gate.RunAsync(_ =>
        {
            secondCompleted = true;
            return Task.CompletedTask;
        });

        await second;
        await first;

        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestCt);
        Assert.True(secondCompleted);
        Assert.False(gate.IsActive);
    }

    [Fact]
    public async Task CancelActiveRun_cancels_inflight_action()
    {
        var gate = new PageReload();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = gate.RunAsync(async ct =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestCt);
        gate.CancelActiveRun();

        await run;
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestCt);
        Assert.False(gate.IsActive);
    }

    [Fact]
    public async Task RunAsync_rethrows_non_cancellation_exception()
    {
        var gate = new PageReload();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync(_ => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task Superseded_run_does_not_invoke_onFinished()
    {
        var gate = new PageReload();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinished = 0;

        _ = gate.RunAsync(
            async ct =>
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            },
            onFinished: () => firstFinished++);

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestCt);

        await gate.RunAsync(_ => Task.CompletedTask);

        await Task.Delay(50, TestCt);
        Assert.Equal(0, firstFinished);
    }

    [Fact]
    public async Task Dispose_cancels_active_run()
    {
        var gate = new PageReload();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = gate.RunAsync(async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestCt);
        gate.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => gate.RunAsync(_ => Task.CompletedTask));
    }
}
