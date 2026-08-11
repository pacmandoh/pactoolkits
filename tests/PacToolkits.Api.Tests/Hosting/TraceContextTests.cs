using System.Net;
using System.Text.Json;
using PacToolkits.Application.Diagnostics;

namespace PacToolkits.Api.Tests;

public sealed class TraceContextTests
{
    [Fact]
    public async Task Traceparent_propagates_into_problem_details_traceId()
    {
        PacActivities.EnsureListening();
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var parent = PacActivities.Desktop.StartActivity("api.trace.parent");
        Assert.NotNull(parent);
        var expectedTrace = parent!.TraceId.ToString();
        var traceParent = PacTrace.FormatTraceParent(parent);
        Assert.NotNull(traceParent);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/ping");
        request.Headers.TryAddWithoutValidation(PacTrace.HeaderName, traceParent);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            doc.RootElement.TryGetProperty("traceId", out var traceEl),
            $"body missing traceId: {doc.RootElement}");
        Assert.Equal(expectedTrace, traceEl.GetString());
    }
}
