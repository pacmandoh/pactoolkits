namespace PacToolkits.Application.DTOs;

/// <summary>数据库连接测试结果</summary>
public sealed record DbTestResult(bool Ok, string Summary);

/// <summary>连接与 Schema 兼容校验结果</summary>
public sealed record DbConnectionValidation(
    bool ConnectionOk,
    string? ConnectionSummary,
    bool SchemaCompatible,
    string? IncompatibleMessage);

/// <summary>数据库连接探测类型</summary>
public enum DbProbeKind
{
    HealthCheck,
    Reconnect
}

/// <summary>数据库连接探测结果</summary>
public sealed record DbProbeReport(DbProbeKind Kind, bool Success, string? Reason);
