namespace PacToolkits.Application.DTOs;

public static class PacApiHeaders
{
    /// <summary>写命令幂等键</summary>
    public const string CommandId = "X-Command-Id";

    /// <summary>AutoRun 跑锁；请求带此头即续期</summary>
    public const string MsfxRunLock = "X-Msfx-Run-Lock";
}

public sealed record PacApiTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string ClientId);

public sealed record UnlockVerifyRequest(string Password);

public sealed record PacApiSystemInfo(
    string Product,
    string ApiVersion,
    string ContractVersion,
    DateTimeOffset Utc);

/// <summary>GET /v1/system/status；database/schema 为服务端诊断字段</summary>
public sealed record PacApiSystemStatus(
    string Status,
    DateTimeOffset Utc,
    string Database,
    string Schema,
    string? SchemaVersion,
    string? Reason);

public sealed record StringListResponse(IReadOnlyList<string> Items);

/// <summary>写命令可缓存的 ProblemDetails 正文</summary>
public sealed record WriteProblemBody(
    int Status,
    string Code,
    string Title,
    string? Detail,
    string TraceId,
    long? CurrentVersion = null,
    IReadOnlyList<StockRowEditConflict>? Conflicts = null);
