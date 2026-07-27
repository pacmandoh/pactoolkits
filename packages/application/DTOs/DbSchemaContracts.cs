using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

/// <summary>数据库结构兼容检查所需的版本范围</summary>
public sealed record DbSchemaVersionContext(
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema,
    string AgentsMinDbSchema,
    string AgentsMaxDbSchema,
    string TargetDbSchemaVersion);

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
