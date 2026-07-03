using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class LogTraceTests
{
    [Fact]
    public void Current_is_null_without_active_scope()
    {
        Assert.Null(LogTrace.Current);
    }

    [Fact]
    public void Begin_sets_current_trace_id()
    {
        using (LogTrace.Begin("trace-a"))
        {
            Assert.Equal("trace-a", LogTrace.Current);
        }

        Assert.Null(LogTrace.Current);
    }

    [Fact]
    public async Task Begin_flows_through_async_await()
    {
        using (LogTrace.Begin("trace-async"))
        {
            await Task.Yield();
            Assert.Equal("trace-async", LogTrace.Current);
        }
    }

    [Fact]
    public void Nested_begin_restores_outer_trace()
    {
        using (LogTrace.Begin("outer"))
        {
            using (LogTrace.Begin("inner"))
            {
                Assert.Equal("inner", LogTrace.Current);
            }

            Assert.Equal("outer", LogTrace.Current);
        }
    }

    [Fact]
    public void CreateId_returns_12_char_value()
    {
        var id = LogTrace.CreateId();

        Assert.Equal(12, id.Length);
        Assert.True(id.All(static c => char.IsAsciiHexDigit(c)));
    }
}
