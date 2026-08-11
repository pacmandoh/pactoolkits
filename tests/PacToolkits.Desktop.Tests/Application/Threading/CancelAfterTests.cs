using PacToolkits.Application.Threading;

namespace PacToolkits.Desktop.Tests;

public sealed class CancelAfterTests
{
    [Fact]
    public async Task DisposeAsync_closes_gate_before_releasing_cts()
    {
        var time = new ControllableTime();
        using var cts = new CancellationTokenSource();
        var cancel = CancelAfter.Schedule(cts, TimeSpan.FromSeconds(5), time);

        // 推进到定时器触发前关闭：迟到回调不得再 Cancel
        await cancel.DisposeAsync();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(cts.IsCancellationRequested);
        cts.Dispose();
    }

    [Fact]
    public async Task Timer_cancels_cts_before_dispose()
    {
        var time = new ControllableTime();
        using var cts = new CancellationTokenSource();
        await using (CancelAfter.Schedule(cts, TimeSpan.FromSeconds(2), time))
        {
            Assert.False(cts.IsCancellationRequested);
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.True(cts.IsCancellationRequested);
        }
    }
}
