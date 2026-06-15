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

    Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(
        DbSchemaVersionContext schemaContext,
        DatabaseMigrationTrigger trigger,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default);

    Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(
        DbSchemaVersionContext schemaContext,
        DatabaseMigrationTrigger trigger,
        PgOptions connectionOptions,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default);

    Task<DbSchemaMigrationPlan> GetSchemaMigrationPlanAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions? connectionOptions = null,
        CancellationToken ct = default);

    Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct);

    Task<ClientAliasSourceLoadResult> LoadClientAliasSourcesAsync(
        PgOptions options,
        CancellationToken ct);
}

public sealed class SettingsService : ISettingsService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionTester _tester;
    private readonly IDbSchemaVersionService _schemaVersion;
    private readonly IDbSchemaMigrationService _schemaMigration;
    private readonly IClientIdReadRepo _clientRepo;
    private readonly IDatabaseAccessGuard _databaseAccessGuard;
    private readonly IDatabaseMigrationPolicyService _migrationPolicy;
    private readonly IDatabaseEnvironmentSettingsService _environmentSettings;

    public SettingsService(
        IDbConfigService dbConfig,
        IDbConnectionTester tester,
        IDbSchemaVersionService schemaVersion,
        IDbSchemaMigrationService schemaMigration,
        IClientIdReadRepo clientRepo,
        IDatabaseAccessGuard databaseAccessGuard,
        IDatabaseMigrationPolicyService migrationPolicy,
        IDatabaseEnvironmentSettingsService environmentSettings)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
        _schemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
        _schemaMigration = schemaMigration ?? throw new ArgumentNullException(nameof(schemaMigration));
        _clientRepo = clientRepo ?? throw new ArgumentNullException(nameof(clientRepo));
        _databaseAccessGuard = databaseAccessGuard ?? throw new ArgumentNullException(nameof(databaseAccessGuard));
        _migrationPolicy = migrationPolicy ?? throw new ArgumentNullException(nameof(migrationPolicy));
        _environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
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

        var statusBeforeMigration = await ReadSchemaStatusAsync(schemaContext, options, ct).ConfigureAwait(false);
        if (statusBeforeMigration.Compatibility == DbSchemaCompatibility.AboveMaximum)
        {
            return new DbConnectionValidationResult(
                ConnectionOk: true,
                ConnectionSummary: test.Summary,
                SchemaMigrationOk: true,
                MigrationSummary: "数据库版本过高，未执行迁移或降级",
                SchemaCompatible: false,
                IncompatibleMessage: BuildIncompatibleMessage(schemaContext, statusBeforeMigration));
        }

        if (statusBeforeMigration.Compatibility is DbSchemaCompatibility.BelowMinimum
            or DbSchemaCompatibility.MetadataMissing)
        {
            var policy = await EvaluatePolicyAsync(
                DatabaseMigrationTrigger.SettingsManual,
                statusBeforeMigration.Compatibility,
                schemaContext,
                userConfirmed: false,
                ciMigrationAuthorized: false,
                connectionOptions: options,
                ct).ConfigureAwait(false);

            if (!policy.ShouldExecuteMigration)
            {
                return new DbConnectionValidationResult(
                    ConnectionOk: true,
                    ConnectionSummary: test.Summary,
                    SchemaMigrationOk: true,
                    MigrationSummary: policy.Reason,
                    SchemaCompatible: false,
                    IncompatibleMessage: BuildIncompatibleMessage(schemaContext, statusBeforeMigration));
            }
        }

        var migration = await EnsureSchemaUpToDateCoreAsync(
            schemaContext,
            DatabaseMigrationTrigger.SettingsManual,
            userConfirmed: false,
            ciMigrationAuthorized: false,
            connectionOptions: options,
            ct).ConfigureAwait(false);
        if (!migration.Ok)
        {
            return new DbConnectionValidationResult(
                ConnectionOk: true,
                ConnectionSummary: test.Summary,
                SchemaMigrationOk: false,
                MigrationSummary: migration.Summary,
                SchemaCompatible: false,
                IncompatibleMessage: migration.Summary);
        }

        var compat = await CheckSchemaCompatibilityAsync(schemaContext, options, ct).ConfigureAwait(false);
        return new DbConnectionValidationResult(
            ConnectionOk: true,
            ConnectionSummary: test.Summary,
            SchemaMigrationOk: true,
            MigrationSummary: migration.Summary,
            SchemaCompatible: compat.Compatible,
            IncompatibleMessage: compat.IncompatibleMessage);
    }

    public Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(
        DbSchemaVersionContext schemaContext,
        DatabaseMigrationTrigger trigger,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default)
        => EnsureSchemaUpToDateCoreAsync(
            schemaContext,
            trigger,
            userConfirmed,
            ciMigrationAuthorized,
            connectionOptions: null,
            ct);

    public Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(
        DbSchemaVersionContext schemaContext,
        DatabaseMigrationTrigger trigger,
        PgOptions connectionOptions,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default)
        => EnsureSchemaUpToDateCoreAsync(
            schemaContext,
            trigger,
            userConfirmed,
            ciMigrationAuthorized,
            connectionOptions,
            ct);

    public Task<DbSchemaMigrationPlan> GetSchemaMigrationPlanAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions? connectionOptions = null,
        CancellationToken ct = default)
        => connectionOptions is null
            ? _schemaMigration.GetPlanAsync(ct, schemaContext.TargetDbSchemaVersion)
            : _schemaMigration.GetPlanAsync(connectionOptions, ct, schemaContext.TargetDbSchemaVersion);

    private async Task<(bool Ok, string Summary)> EnsureSchemaUpToDateCoreAsync(
        DbSchemaVersionContext schemaContext,
        DatabaseMigrationTrigger trigger,
        bool userConfirmed,
        bool ciMigrationAuthorized,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var status = await ReadSchemaStatusAsync(schemaContext, connectionOptions, ct).ConfigureAwait(false);
        if (status.Compatibility == DbSchemaCompatibility.AboveMaximum)
        {
            return (false, $"数据库版本高于当前程序支持范围：当前 {status.CurrentVersion}，最高支持 {status.RequiredMaxVersion}。不会执行自动降级。");
        }

        var policy = await EvaluatePolicyAsync(
            trigger,
            status.Compatibility,
            schemaContext,
            userConfirmed,
            ciMigrationAuthorized,
            connectionOptions,
            ct).ConfigureAwait(false);

        if (policy.Decision == DatabaseMigrationDecision.RequiresConfirmation)
            return (false, policy.Reason);

        if (policy.Decision == DatabaseMigrationDecision.ReadOnlyRequired)
            return (false, policy.Reason);

        if (!policy.ShouldExecuteMigration)
            return (true, policy.Reason);

        var migration = connectionOptions is null
            ? await _schemaMigration.EnsureUpToDateAsync(ct, schemaContext.TargetDbSchemaVersion).ConfigureAwait(false)
            : await _schemaMigration.EnsureUpToDateAsync(connectionOptions, ct, schemaContext.TargetDbSchemaVersion).ConfigureAwait(false);
        var summary =
            $"before={migration.BeforeVersion ?? "unknown"} -> after={migration.AfterVersion ?? "unknown"}（applied={migration.AppliedCount}, skipped={migration.SkippedCount}）";
        return (true, summary);
    }

    public Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
        => CheckSchemaCompatibilityAsync(schemaContext, connectionOptions: null, ct);

    public async Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var snapshot = await ReadSchemaStatusAsync(schemaContext, connectionOptions, ct).ConfigureAwait(false);
        if (snapshot.Satisfied)
            return (true, null);

        return (false, BuildIncompatibleMessage(schemaContext, snapshot));
    }

    public Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
        => ReadSchemaStatusAsync(schemaContext, connectionOptions: null, ct);

    public async Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var uiMin = DbSchemaCompat.NormalizeBound(schemaContext.UiMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var uiMax = DbSchemaCompat.NormalizeBound(schemaContext.UiMaxDbSchema, schemaContext.TargetDbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(schemaContext.AgentMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var agentMax = DbSchemaCompat.NormalizeBound(schemaContext.AgentMaxDbSchema, schemaContext.TargetDbSchemaVersion);
        var requiredMin = DbSchemaCompat.GetRequiredMin(uiMin, agentMin);
        var requiredMax = DbSchemaCompat.GetRequiredMax(uiMax, agentMax);
        var localTarget = DbSchemaCompat.NormalizeBound(
            schemaContext.TargetDbSchemaVersion,
            schemaContext.TargetDbSchemaVersion);
        var schema = connectionOptions is null
            ? await _schemaVersion.TryReadSchemaVersionAsync(ct).ConfigureAwait(false)
            : await _schemaVersion.TryReadSchemaVersionAsync(connectionOptions, ct).ConfigureAwait(false);
        var compatibility = BuildCompatibility(schema, requiredMin, requiredMax);

        if (ShouldApplyDatabaseGuard(connectionOptions))
            ApplyDatabaseGuard(compatibility);

        var manualMigrationPolicy = await EvaluatePolicyAsync(
            DatabaseMigrationTrigger.SettingsManual,
            compatibility.Status,
            schemaContext,
            userConfirmed: false,
            ciMigrationAuthorized: false,
            connectionOptions,
            ct).ConfigureAwait(false);

        var current = schema.Value ?? string.Empty;
        var updatable = (compatibility.IsTooLow || compatibility.IsMetadataMissing)
                        && (compatibility.IsMetadataMissing || IsSchemaUpdatable(current, localTarget))
                        && (manualMigrationPolicy.ShouldExecuteMigration
                            || manualMigrationPolicy.Decision == DatabaseMigrationDecision.RequiresConfirmation);
        return new DbSchemaStatusSnapshot(
            SchemaOk: schema.Ok,
            CurrentVersion: current,
            Reason: schema.Ok ? null : schema.Reason ?? "读取失败",
            TargetVersion: localTarget,
            RequiredMinVersion: requiredMin,
            RequiredMaxVersion: requiredMax,
            Compatibility: compatibility.Status,
            Satisfied: compatibility.IsCompatible,
            Updatable: updatable,
            ManualMigrationPolicy: manualMigrationPolicy);
    }

    private async Task<DatabaseMigrationPolicyResult> EvaluatePolicyAsync(
        DatabaseMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        DbSchemaVersionContext schemaContext,
        bool userConfirmed,
        bool ciMigrationAuthorized,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var environment = connectionOptions is null
            ? await _environmentSettings.TryReadAsync(ct).ConfigureAwait(false)
            : await _environmentSettings.TryReadAsync(connectionOptions, ct).ConfigureAwait(false);
        return _migrationPolicy.Evaluate(new DatabaseMigrationEvaluationContext(
            trigger,
            compatibility,
            schemaContext.ReleaseChannel,
            schemaContext.MigrationPolicy,
            environment,
            userConfirmed,
            ciMigrationAuthorized));
    }

    private static DbSchemaCompatibilityResult BuildCompatibility(
        DbSchemaVersionReadResult schema,
        string requiredMin,
        string requiredMax)
    {
        if (schema.Ok)
            return DbSchemaCompat.Evaluate(schema.Value, requiredMin, requiredMax);

        if (schema.IsMetadataMissing)
        {
            return new DbSchemaCompatibilityResult(
                DbSchemaCompatibility.MetadataMissing,
                schema.Value ?? string.Empty,
                requiredMin,
                requiredMax,
                schema.Reason ?? "数据库元数据缺失，需要初始化");
        }

        return new DbSchemaCompatibilityResult(
            DbSchemaCompatibility.Unknown,
            schema.Value ?? string.Empty,
            requiredMin,
            requiredMax,
            schema.Reason ?? "读取失败");
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

    private void ApplyDatabaseGuard(DbSchemaCompatibilityResult compatibility)
    {
        if (compatibility.IsCompatible)
            _databaseAccessGuard.Clear();
        else
            _databaseAccessGuard.Block(compatibility.Message);
    }

    private bool ShouldApplyDatabaseGuard(PgOptions? connectionOptions)
    {
        if (connectionOptions is null)
            return true;

        var current = _dbConfig.Current;
        return string.Equals(connectionOptions.Host, current.Host, StringComparison.OrdinalIgnoreCase)
               && connectionOptions.Port == current.Port
               && string.Equals(connectionOptions.Database, current.Database, StringComparison.Ordinal)
               && string.Equals(connectionOptions.Username, current.Username, StringComparison.Ordinal)
               && string.Equals(connectionOptions.Password, current.Password, StringComparison.Ordinal);
    }

    private static string BuildIncompatibleMessage(
        DbSchemaVersionContext schemaContext,
        DbSchemaStatusSnapshot snapshot)
    {
        var uiMin = DbSchemaCompat.NormalizeBound(schemaContext.UiMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var uiMax = DbSchemaCompat.NormalizeBound(schemaContext.UiMaxDbSchema, schemaContext.TargetDbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(schemaContext.AgentMinDbSchema, schemaContext.TargetDbSchemaVersion);
        var agentMax = DbSchemaCompat.NormalizeBound(schemaContext.AgentMaxDbSchema, schemaContext.TargetDbSchemaVersion);
        return DbSchemaCompat.BuildIncompatibleMessage(
            snapshot.SchemaOk,
            snapshot.CurrentVersion,
            snapshot.Reason,
            uiMin,
            agentMin,
            uiMax,
            agentMax,
            snapshot.RequiredMinVersion,
            snapshot.RequiredMaxVersion);
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
