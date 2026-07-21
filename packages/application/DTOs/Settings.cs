using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

/// <summary>
/// Schema 兼容检查所需的版本门槛上下文
/// </summary>
public sealed record DbSchemaVersionContext(
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema,
    string AgentsMinDbSchema,
    string AgentsMaxDbSchema,
    string TargetDbSchemaVersion,
    string ReleaseChannel = "stable",
    string MigrationPolicy = DbMigrationPolicies.StableOnly);

/// <summary>
/// 连接 + Schema 迁移/兼容综合校验结果
/// </summary>
public sealed record DbConnectionValidation(
    bool ConnectionOk,
    string? ConnectionSummary,
    bool SchemaMigrationOk,
    string? MigrationSummary,
    bool SchemaCompatible,
    string? IncompatibleMessage);

public sealed record DbSchemaStatusSnapshot(
    bool SchemaOk,
    string? CurrentVersion,
    string? Reason,
    string TargetVersion,
    string RequiredMinVersion,
    string RequiredMaxVersion,
    DbSchemaCompatibility Compatibility,
    bool Satisfied,
    bool Updatable,
    DbMigrationOutcome ManualMigrationPolicy,
    string? IncompatibleMessage);

/// <summary>
/// 别名编辑可用的机器名来源
/// </summary>
public sealed record ClientAliasSources(
    bool IsDbConnected,
    IReadOnlyList<string> ClientMachines);

public sealed record DbSchemaVersionRead(
    bool Ok,
    string? Value,
    string? Reason,
    bool IsMetadataMissing = false);
