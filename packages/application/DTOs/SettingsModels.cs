using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

public sealed record DbSchemaVersionContext(
    string UiMinDbSchema,
    string UiMaxDbSchema,
    string AgentMinDbSchema,
    string AgentMaxDbSchema,
    string TargetDbSchemaVersion,
    string ReleaseChannel = "stable",
    string MigrationPolicy = DatabaseMigrationPolicies.StableOnly);

public sealed record DbConnectionValidationResult(
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
    DatabaseMigrationPolicyResult ManualMigrationPolicy,
    string? IncompatibleMessage);

public sealed record ClientAliasSourceLoadResult(
    bool IsDbConnected,
    IReadOnlyList<string> ClientMachines);
