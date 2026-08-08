using System.Security.Claims;
using System.Text.Json;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Changes;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Endpoints;

/// <summary>变更 SSE 与 watermark 快照</summary>
public static class ChangeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static IEndpointRouteBuilder MapChanges(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/changes/watermarks", ListWatermarks)
            .RequireAuthorization(AuthPolicies.Read);

        routes.MapGet("/v1/changes/stream", StreamChanges)
            .RequireAuthorization(AuthPolicies.Read);

        return routes;
    }

    private static async Task<IResult> ListWatermarks(IChangeWatermarkRepo repo, CancellationToken ct)
    {
        var items = await repo.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new ChangeWatermarksResponse(items));
    }

    private static async Task StreamChanges(
        HttpContext http,
        ChangeBus bus,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientId = user.FindFirstValue(JwtTokenIssuer.ClientIdClaim);
        if (string.IsNullOrEmpty(clientId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!bus.TrySubscribe(clientId, out var sub))
        {
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await http.Response.WriteAsJsonAsync(
                new ApiProblem(
                    StatusCodes.Status429TooManyRequests,
                    ApiErrors.RateLimited,
                    "Too many change stream subscriptions for this client",
                    http.TraceIdentifier),
                ct).ConfigureAwait(false);
            return;
        }

        await using var _ = sub;

        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.Connection = "keep-alive";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        await http.Response.WriteAsync("event: ready\ndata: {}\n\n", ct).ConfigureAwait(false);
        await http.Response.Body.FlushAsync(ct).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);
        var token = linked.Token;

        try
        {
            // SingleReader：heartbeat 用 CancelAfter 取消 WaitToRead，禁止二次并发 Wait
            while (!token.IsCancellationRequested)
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                waitCts.CancelAfter(HeartbeatInterval);

                bool hasData;
                try
                {
                    hasData = await sub.Reader.WaitToReadAsync(waitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    var hb = JsonSerializer.Serialize(
                        new ChangeHeartbeat(DateTimeOffset.UtcNow),
                        JsonOptions);
                    await http.Response.WriteAsync($"event: heartbeat\ndata: {hb}\n\n", token)
                        .ConfigureAwait(false);
                    await http.Response.Body.FlushAsync(token).ConfigureAwait(false);
                    continue;
                }

                if (!hasData)
                {
                    break;
                }

                while (sub.Reader.TryRead(out var topic))
                {
                    var payload = JsonSerializer.Serialize(new ChangeEvent(topic), JsonOptions);
                    await http.Response.WriteAsync($"event: change\ndata: {payload}\n\n", token)
                        .ConfigureAwait(false);
                }

                await http.Response.Body.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed record ChangeWatermarksResponse(IReadOnlyList<ChangeWatermarkItem> Items);

public sealed record ChangeEvent(string Topic);

public sealed record ChangeHeartbeat(DateTimeOffset Utc);
