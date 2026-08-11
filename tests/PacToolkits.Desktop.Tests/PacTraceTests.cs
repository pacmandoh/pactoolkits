using System.Diagnostics;
using PacToolkits.Application.Diagnostics;

namespace PacToolkits.Desktop.Tests;

[Collection(nameof(PacActivitiesCollection))]
public sealed class PacTraceTests
{
    [Fact]
    public void Inject_writes_w3c_traceparent_from_current_activity()
    {
        PacActivities.EnsureListening();
        using var activity = PacActivities.Desktop.StartActivity("trace.inject");
        Assert.NotNull(activity);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/v1/ping");
        PacTrace.Inject(request);

        Assert.True(request.Headers.TryGetValues(PacTrace.HeaderName, out var values));
        var header = Assert.Single(values!);
        Assert.Equal(activity!.Id, header);
        Assert.True(ActivityContext.TryParse(header, null, out var ctx));
        Assert.Equal(activity.TraceId, ctx.TraceId);
    }

    [Fact]
    public void FormatTraceParent_null_without_activity()
    {
        var prev = Activity.Current;
        try
        {
            Activity.Current = null;
            Assert.Null(PacTrace.FormatTraceParent());
        }
        finally
        {
            Activity.Current = prev;
        }
    }
}

[CollectionDefinition(nameof(PacActivitiesCollection), DisableParallelization = true)]
public sealed class PacActivitiesCollection;
