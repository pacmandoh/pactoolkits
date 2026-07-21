using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

public sealed record DbSchemaVersionContext(
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema,
    string AgentsMinDbSchema,
    string AgentsMaxDbSchema,
    string TargetDbSchemaVersion,
    string ReleaseChannel = "stable",
    string MigrationPolicy = DbMigrationPolicies.StableOnly);

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

public sealed record ClientAliasSources(
    bool IsDbConnected,
    IReadOnlyList<string> ClientMachines);

public sealed record DbSchemaVersionRead(
    bool Ok,
    string? Value,
    string? Reason,
    bool IsMetadataMissing = false);
