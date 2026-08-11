using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

/// <summary>数据库结构兼容检查所需的版本范围（Desktop 业务访问用 schema 区间）</summary>
public sealed record DbSchemaVersionContext(
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema,
    string TargetDbSchemaVersion)
{
    public string Min => Trim(DesktopMinDbSchema);

    public string Max => Trim(DesktopMaxDbSchema);

    public string Target => Trim(TargetDbSchemaVersion);

    private static string Trim(string? value)
        => (value ?? string.Empty).Trim();
}

/// <summary>数据库 Schema 版本读取结果</summary>
public sealed record DbSchemaVersionRead(
    bool Ok,
    string? Value,
    string? Reason,
    bool IsMetadataMissing = false);

/// <summary>数据库结构兼容状态</summary>
public sealed record DbSchemaStatusSnapshot(
    bool SchemaOk,
    string? CurrentVersion,
    string? Reason,
    string TargetVersion,
    string RequiredMinVersion,
    string RequiredMaxVersion,
    DbSchemaCompatibility Compatibility,
    bool Satisfied,
    string? IncompatibleMessage);
