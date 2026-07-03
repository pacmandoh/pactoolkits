using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class TaskObserveTests
{
    [Fact]
    public void Unwrap_returns_single_inner_from_aggregate()
    {
        var inner = new InvalidOperationException("boom");
        var aggregate = new AggregateException(inner);

        var unwrapped = TaskObserve.Unwrap(aggregate);

        Assert.Same(inner, unwrapped);
    }

    [Fact]
    public void Unwrap_returns_non_aggregate_unchanged()
    {
        var ex = new InvalidOperationException("boom");

        Assert.Same(ex, TaskObserve.Unwrap(ex));
    }

    [Fact]
    public void Observe_marks_faulted_task_as_observed()
    {
        var task = Task.FromException(new InvalidOperationException("boom"));

        TaskObserve.Observe(task, "TestModule", "test.detached.fail");

        var ex = task.Exception;
        Assert.NotNull(ex);
        Assert.Single(ex.InnerExceptions);
    }

    [Fact]
    public async Task Observe_awaits_running_task_without_unobserved_fault()
    {
        var task = Task.Run(async () =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("late boom");
        });

        TaskObserve.Observe(task, "TestModule", "test.detached.late_fail");

        try
        {
            await task;
        }
        catch (InvalidOperationException)
        {
        }

        Assert.True(task.IsFaulted);
        _ = task.Exception;
    }

    [Fact]
    public void Observe_ignores_canceled_task()
    {
        var task = Task.FromCanceled(new CancellationToken(true));

        TaskObserve.Observe(task, "TestModule", "test.detached.cancel");

        Assert.True(task.IsCanceled);
    }
}
