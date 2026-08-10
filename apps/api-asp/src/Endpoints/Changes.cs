using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
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

        // 建连只验一次 JWT：流寿命不超过 exp；客户端换票后重建连接
        if (!TryGetJwtExpires(user, out var expiresAt))
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

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.RequestAborted);
        // Bearer 有 ClockSkew，流仍按绝对 exp 收口；已过期则立刻取消
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        linked.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        var streamCt = linked.Token;

        await http.Response.WriteAsync("event: ready\ndata: {}\n\n", streamCt).ConfigureAwait(false);
        await http.Response.Body.FlushAsync(streamCt).ConfigureAwait(false);

        try
        {
            // SingleReader：heartbeat 用 CancelAfter 取消 WaitToRead，禁止二次并发 Wait
            while (!streamCt.IsCancellationRequested)
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(streamCt);
                waitCts.CancelAfter(HeartbeatInterval);

                bool hasData;
                try
                {
                    hasData = await sub.Reader.WaitToReadAsync(waitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!streamCt.IsCancellationRequested)
                {
                    var hb = JsonSerializer.Serialize(
                        new ChangeHeartbeat(DateTimeOffset.UtcNow),
                        JsonOptions);
                    await http.Response.WriteAsync($"event: heartbeat\ndata: {hb}\n\n", streamCt)
                        .ConfigureAwait(false);
                    await http.Response.Body.FlushAsync(streamCt).ConfigureAwait(false);
                    continue;
                }

                if (!hasData)
                {
                    break;
                }

                while (sub.Reader.TryRead(out var topic))
                {
                    var payload = JsonSerializer.Serialize(new ChangeEvent(topic), JsonOptions);
                    await http.Response.WriteAsync($"event: change\ndata: {payload}\n\n", streamCt)
                        .ConfigureAwait(false);
                }

                await http.Response.Body.FlushAsync(streamCt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool TryGetJwtExpires(ClaimsPrincipal user, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        var exp = user.FindFirstValue(JwtRegisteredClaimNames.Exp);
        if (string.IsNullOrWhiteSpace(exp)
            || !long.TryParse(exp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public sealed record ChangeWatermarksResponse(IReadOnlyList<ChangeWatermarkItem> Items);

public sealed record ChangeEvent(string Topic);

public sealed record ChangeHeartbeat(DateTimeOffset Utc);
