namespace PacToolkits.Application.DTOs;

/// <summary>数据库 Schema 版本读取结果</summary>
public sealed record DbSchemaVersionRead(
    bool Ok,
    string? Value,
    string? Reason,
    bool IsMetadataMissing = false);
