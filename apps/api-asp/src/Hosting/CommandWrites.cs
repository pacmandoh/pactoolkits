using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PacToolkits.Api.Auth;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Api.Hosting;

/// <summary>写命令的幂等执行入口</summary>
public static class CommandWrites
{
    /// <summary>会改库的写；Claim、业务与 Complete 同一库事务</summary>
    public static async Task<IResult> ExecuteAsync(
        HttpContext http,
        IDb db,
        ICommandDedup dedup,
        string operation,
        ReadOnlyMemory<byte> requestBody,
        Func<CancellationToken, Task<CommandDedupEntry>> action,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(dedup);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);

        if (!ApiProblems.TryGetCommandId(http.Request, out var commandId, out var error))
        {
            return error!;
        }

        var clientId = http.User.FindFirstValue(JwtTokenIssuer.ClientIdClaim);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ApiProblems.BadRequest(
                http,
                title: "Missing client",
                detail: "JWT client_id is required for write commands");
        }

        var key = new CommandDedupKey(
            ClientId: clientId,
            Operation: operation,
            CommandId: commandId,
            RequestDigest: CommandDigest.Sha256Hex(requestBody.Span));

        return await db.WithTransaction(
                async (_, _, token) =>
                {
                    var claim = await dedup.ClaimAsync(key, token).ConfigureAwait(false);
                    switch (claim.Outcome)
                    {
                        case CommandDedupClaim.Completed:
                            return ToResult(claim.Cached!);
                        case CommandDedupClaim.InProgress:
                            return ApiProblems.ServiceUnavailable(
                                http,
                                title: "Command in progress",
                                detail: "Retry after the prior attempt finishes",
                                retryAfter: TimeSpan.FromSeconds(1));
                        case CommandDedupClaim.PayloadMismatch:
                            return ApiProblems.Conflict(
                                http,
                                title: "Command payload mismatch",
                                detail: "Same CommandId with a different request body",
                                code: "payload_mismatch");
                        case CommandDedupClaim.Acquired:
                            break;
                        default:
                            throw new InvalidOperationException($"unexpected claim {claim.Outcome}");
                    }

                    CommandDedupEntry write;
                    try
                    {
                        write = await action(token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 事务已中止时再 DELETE 会抛 25P02 盖住原异常；Pg 靠回滚释放 claim
                        if (dedup.ReleaseOnFailure)
                        {
                            await dedup.ReleaseAsync(key, CancellationToken.None).ConfigureAwait(false);
                        }

                        throw;
                    }

                    // Complete 不用请求 ct；取消后也记下完成态，随外层事务提交
                    await dedup.CompleteAsync(key, write, CancellationToken.None).ConfigureAwait(false);
                    return ToResult(write);
                },
                ct: ct)
            .ConfigureAwait(false);
    }

    /// <summary>查重、预览等只读 POST；要 CommandId，但不 Claim、不 Complete，结果也不回放</summary>
    public static async Task<IResult> ExecuteFreshAsync(
        HttpContext http,
        Func<CancellationToken, Task<CommandDedupEntry>> action,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(action);

        if (!ApiProblems.TryGetCommandId(http.Request, out _, out var error))
        {
            return error!;
        }

        var clientId = http.User.FindFirstValue(JwtTokenIssuer.ClientIdClaim);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return ApiProblems.BadRequest(
                http,
                title: "Missing client",
                detail: "JWT client_id is required for write commands");
        }

        var write = await action(ct).ConfigureAwait(false);
        return ToResult(write);
    }

    public static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    public static bool TryDeserialize<T>(
        ReadOnlySpan<byte> utf8Json,
        JsonTypeInfo<T> typeInfo,
        out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize(utf8Json, typeInfo);
            return value is not null;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    public static CommandDedupEntry Json<T>(
        int statusCode,
        T body,
        JsonTypeInfo<T> typeInfo,
        string contentType = "application/json; charset=utf-8")
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, typeInfo);
        return new CommandDedupEntry(statusCode, contentType, bytes);
    }

    public static CommandDedupEntry Empty(int statusCode)
        => new(statusCode, ContentType: null, Body: null);

    public static CommandDedupEntry Problem(
        HttpContext http,
        int status,
        string title,
        string detail,
        long? currentVersion = null,
        IReadOnlyList<StockRowEditConflict>? conflicts = null,
        string? code = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        var problem = new WriteProblemBody(
            Status: status,
            Code: code
                  ?? (status == StatusCodes.Status409Conflict
                      ? ApiErrors.Conflict
                      : ApiErrors.ValidationFailed),
            Title: title,
            Detail: detail,
            TraceId: ApiProblem.ResolveTraceId(http),
            CurrentVersion: currentVersion,
            Conflicts: conflicts);
        return Json(
            status,
            problem,
            PacJsonContext.Default.WriteProblemBody,
            contentType: "application/problem+json");
    }

    /// <summary>ArgumentException 返回 400；部分 InvalidOperationException 返回 404/409；其余原样抛出</summary>
    public static bool TryMapBusinessException(
        HttpContext http,
        Exception ex,
        out CommandDedupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is ArgumentException arg)
        {
            entry = Problem(
                http,
                StatusCodes.Status400BadRequest,
                "Invalid argument",
                string.IsNullOrWhiteSpace(arg.Message) ? "Invalid argument" : arg.Message);
            return true;
        }

        if (ex is InvalidOperationException inv)
        {
            var message = string.IsNullOrWhiteSpace(inv.Message) ? "Invalid operation" : inv.Message;
            if (LooksNotFound(message))
            {
                entry = Problem(
                    http,
                    StatusCodes.Status404NotFound,
                    "Not found",
                    message,
                    code: ApiErrors.NotFound);
                return true;
            }

            if (LooksConflict(message))
            {
                entry = Problem(
                    http,
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    message);
                return true;
            }
        }

        entry = default!;
        return false;
    }

    private static bool LooksNotFound(string message)
        => message.Contains("不存在", StringComparison.Ordinal)
           || message.Contains("未找到", StringComparison.Ordinal)
           || message.Contains("已被移除", StringComparison.Ordinal);

    private static bool LooksConflict(string message)
        => message.Contains("无需", StringComparison.Ordinal)
           || message.Contains("未检测到", StringComparison.Ordinal)
           || message.Contains("与当前一致", StringComparison.Ordinal);

    public static CommandDedupEntry BadJson(HttpContext http)
        => Problem(http, StatusCodes.Status400BadRequest, "Invalid body", "JSON body required");

    private static IResult ToResult(CommandDedupEntry write)
    {
        if (write.Body is null || write.Body.Length == 0)
        {
            return Results.StatusCode(write.StatusCode);
        }

        var text = Encoding.UTF8.GetString(write.Body);
        return Results.Content(
            text,
            write.ContentType ?? "application/json; charset=utf-8",
            statusCode: write.StatusCode);
    }
}
