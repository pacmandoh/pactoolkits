using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

public interface ISettingsService
{
    Task SaveDatabaseConfigAsync(PgOptions options, CancellationToken ct);

    Task<DbConnectionValidationResult> ValidateDatabaseConnectionAsync(
        PgOptions options,
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<(bool Ok, string? Summary)> TestConnectionAsync(PgOptions options, CancellationToken ct);

    Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(string targetVersion, CancellationToken ct);

    Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<ClientAliasSourceLoadResult> LoadClientAliasSourcesAsync(PgOptions options, CancellationToken ct);
}

public sealed class SettingsService : ISettingsService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionTester _tester;
    private readonly IDbSchemaVersionService _schemaVersion;
    private readonly IDbSchemaMigrationService _schemaMigration;
    private readonly IClientIdReadRepo _clientRepo;

    public SettingsService(
        IDbConfigService dbConfig,
        IDbConnectionTester tester,
        IDbSchemaVersionService schemaVersion,
        IDbSchemaMigrationService schemaMigration,
        IClientIdReadRepo clientRepo)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
        _schemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
        _schemaMigration = schemaMigration ?? throw new ArgumentNullException(nameof(schemaMigration));
        _clientRepo = clientRepo ?? throw new ArgumentNullException(nameof(clientRepo));
    }

    public Task SaveDatabaseConfigAsync(PgOptions options, CancellationToken ct)
        => _dbConfig.SaveAndApplyAsync(options, ct);

    public async Task<(bool Ok, string? Summary)> TestConnectionAsync(PgOptions options, CancellationToken ct)
    {
        var result = await _tester.TestAsync(options, ct).ConfigureAwait(false);
        return (result.Ok, result.Summary);
    }

    public async Task<DbConnectionValidationResult> ValidateDatabaseConnectionAsync(
        PgOptions options,
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
    {
        var test = await TestConnectionAsync(options, ct).ConfigureAwait(false);
        if (!test.Ok)
        {
            return new DbConnectionValidationResult(
                ConnectionOk: false,
                ConnectionSummary: test.Summary,
                SchemaMigrationOk: false,
                MigrationSummary: null,
                SchemaCompatible: false,
                IncompatibleMessage: null);
        }

        var migration = await EnsureSchemaUpToDateAsync(schemaContext.TargetDbSchemaVersion, ct).ConfigureAwait(false);
        if (!migration.Ok)
        {
            return new DbConnectionValidationResult(
                ConnectionOk: true,
                ConnectionSummary: test.Summary,
                SchemaMigrationOk: false,
                MigrationSummary: migration.Summary,
                SchemaCompatible: false,
                IncompatibleMessage: null);
        }

        var compat = await CheckSchemaCompatibilityAsync(schemaContext, ct).ConfigureAwait(false);
        return new DbConnectionValidationResult(
            ConnectionOk: true,
            ConnectionSummary: test.Summary,
            SchemaMigrationOk: true,
            MigrationSummary: migration.Summary,
            SchemaCompatible: compat.Compatible,
            IncompatibleMessage: compat.IncompatibleMessage);
    }

    public async Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(string targetVersion, CancellationToken ct)
    {
        var migration = await _schemaMigration.EnsureUpToDateAsync(ct, targetVersion).ConfigureAwait(false);
        var summary =
            $"before={migration.BeforeVersion ?? "unknown"} -> after={migration.AfterVersion ?? "unknown"}（applied={migration.AppliedCount}, skipped={migration.SkippedCount}）";
        return (true, summary);
    }

    public async Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
    {
        var uiMin = DbSchemaCompat.NormalizeBound(schemaContext.UiMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(schemaContext.AgentMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var requiredMin = DbSchemaCompat.GetRequiredMin(uiMin, agentMin);

        var schema = await _schemaVersion.TryReadSchemaVersionAsync(ct).ConfigureAwait(false);
        var db = schema.value ?? string.Empty;
        var uiOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, uiMin);
        var agentOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, agentMin);
        if (uiOk && agentOk)
            return (true, null);

        var message = DbSchemaCompat.BuildIncompatibleMessage(
            schema.ok,
            schema.value,
            schema.reason,
            uiMin,
            agentMin,
            requiredMin);

        return (false, message);
    }

    public async Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
    {
        var schema = await _schemaVersion.TryReadSchemaVersionAsync(ct).ConfigureAwait(false);
        var requiredMin = DbSchemaCompat.GetRequiredMin(
            DbSchemaCompat.NormalizeBound(schemaContext.UiMinDbSchema, schemaContext.TargetDbSchemaVersion),
            DbSchemaCompat.NormalizeBound(schemaContext.AgentMinDbSchema, schemaContext.TargetDbSchemaVersion));
        var localTarget = DbSchemaCompat.NormalizeBound(
            schemaContext.TargetDbSchemaVersion,
            schemaContext.TargetDbSchemaVersion);

        if (!schema.ok)
        {
            return new DbSchemaStatusSnapshot(
                SchemaOk: false,
                CurrentVersion: schema.value,
                Reason: schema.reason ?? "读取失败",
                TargetVersion: localTarget,
                RequiredMinVersion: requiredMin,
                Satisfied: false,
                Updatable: false);
        }

        var current = schema.value ?? string.Empty;
        var satisfied = DbSchemaCompat.IsSemVerAtLeast(current, requiredMin);
        var updatable = IsSchemaUpdatable(current, localTarget);
        return new DbSchemaStatusSnapshot(
            SchemaOk: true,
            CurrentVersion: current,
            Reason: null,
            TargetVersion: localTarget,
            RequiredMinVersion: requiredMin,
            Satisfied: satisfied,
            Updatable: updatable);
    }

    public async Task<ClientAliasSourceLoadResult> LoadClientAliasSourcesAsync(PgOptions options, CancellationToken ct)
    {
        try
        {
            var clients = await _clientRepo.GetDistinctClientIdsAsync(options, ct).ConfigureAwait(false);
            var machines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in clients)
            {
                var machine = ExtractMachine(raw);
                if (machine.Length > 0)
                    machines.Add(machine);
            }

            return new ClientAliasSourceLoadResult(
                IsDbConnected: true,
                ClientMachines: machines.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch
        {
            return new ClientAliasSourceLoadResult(
                IsDbConnected: false,
                ClientMachines: Array.Empty<string>());
        }
    }

    private static bool IsSchemaUpdatable(string? currentVersion, string? localTargetVersion)
    {
        if (!DbSchemaCompat.TryParseSemVer(currentVersion ?? string.Empty, out var current))
            return false;
        if (!DbSchemaCompat.TryParseSemVer(localTargetVersion ?? string.Empty, out var target))
            return false;
        return DbSchemaCompat.CompareSemVer(current, target) < 0;
    }

    private static string ExtractMachine(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;

        var parsed = ClientParser.Parse(text);
        return (parsed.Machine ?? text).Trim();
    }
}
