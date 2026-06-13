namespace PacToolkits.Application.DTOs;

public sealed record DbSchemaVersionContext(
    string UiMinDbSchema,
    string AgentMinDbSchema,
    string TargetDbSchemaVersion);

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
    bool Satisfied,
    bool Updatable);

public sealed record ClientAliasSourceLoadResult(
    bool IsDbConnected,
    IReadOnlyList<string> ClientMachines);
