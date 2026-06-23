using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

public interface ISettingsService
{
    Task SaveDbConfigAsync(PgOptions options, CancellationToken ct);

    Task<DbConnectionValidationResult> ValidateDbConnectionAsync(
        PgOptions options,
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(
        DbSchemaVersionContext schemaContext,
        DbMigrationTrigger trigger,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default);

    Task<(bool Ok, string Summary)> EnsureSchemaUpToDateAsync(
        DbSchemaVersionContext schemaContext,
        DbMigrationTrigger trigger,
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
        PgOptions connectionOptions,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        DbMigrationTrigger trigger,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct);

    Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        DbMigrationTrigger trigger,
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
    private readonly IDbAccessGuard _accessGuard;
    private readonly IDbMigrationPolicyService _migrationPolicy;
    private readonly IDbEnvSettingsService _envSettings;

    public SettingsService(
        IDbConfigService dbConfig,
        IDbConnectionTester tester,
        IDbSchemaVersionService schemaVersion,
        IDbSchemaMigrationService schemaMigration,
        IClientIdReadRepo clientRepo,
        IDbAccessGuard accessGuard,
        IDbMigrationPolicyService migrationPolicy,
        IDbEnvSettingsService envSettings)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _tester = tester ?? throw new ArgumentNullException(nameof(tester));
        _schemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
        _schemaMigration = schemaMigration ?? throw new ArgumentNullException(nameof(schemaMigration));
        _clientRepo = clientRepo ?? throw new ArgumentNullException(nameof(clientRepo));
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
        _migrationPolicy = migrationPolicy ?? throw new ArgumentNullException(nameof(migrationPolicy));
        _envSettings = envSettings ?? throw new ArgumentNullException(nameof(envSettings));
    }

    public Task SaveDbConfigAsync(PgOptions options, CancellationToken ct)
        => _dbConfig.SaveAndApplyAsync(options, ct);

    private async Task<(bool Ok, string? Summary)> TestConnectionAsync(PgOptions options, CancellationToken ct)
    {
        var result = await _tester.TestAsync(options, ct).ConfigureAwait(false);
        return (result.Ok, result.Summary);
    }

    public async Task<DbConnectionValidationResult> ValidateDbConnectionAsync(
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
                DbMigrationTrigger.SettingsManual,
                statusBeforeMigration.Compatibility,
                schemaContext,
                userConfirmed: false,
                ciMigrationAuthorized: false,
                connectionOptions: options,
                ct).ConfigureAwait(false);

            if (!policy.RunMigration)
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
            DbMigrationTrigger.SettingsManual,
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
        DbMigrationTrigger trigger,
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
        DbMigrationTrigger trigger,
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
        DbMigrationTrigger trigger,
        bool userConfirmed,
        bool ciMigrationAuthorized,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var status = await ReadSchemaStatusAsync(schemaContext, trigger, connectionOptions, ct).ConfigureAwait(false);
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

        if (policy.Decision == DbMigrationDecision.RequiresConfirmation)
        {
            return (false, policy.Reason);
        }

        if (policy.Decision == DbMigrationDecision.ReadOnlyRequired)
        {
            return (false, policy.Reason);
        }

        if (!policy.RunMigration)
        {
            return (true, policy.Reason);
        }

        var migration = connectionOptions is null
            ? await _schemaMigration.EnsureUpToDateAsync(ct, schemaContext.TargetDbSchemaVersion).ConfigureAwait(false)
            : await _schemaMigration.EnsureUpToDateAsync(connectionOptions, ct, schemaContext.TargetDbSchemaVersion).ConfigureAwait(false);
        var summary =
            $"before={migration.BeforeVersion ?? "unknown"} -> after={migration.AfterVersion ?? "unknown"}（applied={migration.AppliedCount}, skipped={migration.SkippedCount}）";
        return (true, summary);
    }

    public async Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct)
        => await CheckSchemaCompatibilityCoreAsync(schemaContext, connectionOptions, ct).ConfigureAwait(false);

    private async Task<(bool Compatible, string? IncompatibleMessage)> CheckSchemaCompatibilityCoreAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var snapshot = await ReadSchemaStatusAsync(
            schemaContext,
            DbMigrationTrigger.SettingsManual,
            connectionOptions,
            ct).ConfigureAwait(false);
        if (snapshot.Satisfied)
        {
            return (true, null);
        }

        return (false, snapshot.IncompatibleMessage ?? BuildIncompatibleMessage(schemaContext, snapshot));
    }

    public Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        CancellationToken ct)
        => ReadSchemaStatusAsync(
            schemaContext,
            DbMigrationTrigger.SettingsManual,
            connectionOptions: null,
            ct);

    public Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        DbMigrationTrigger trigger,
        CancellationToken ct)
        => ReadSchemaStatusAsync(schemaContext, trigger, connectionOptions: null, ct);

    public Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        PgOptions connectionOptions,
        CancellationToken ct)
        => ReadSchemaStatusAsync(
            schemaContext,
            DbMigrationTrigger.SettingsManual,
            connectionOptions,
            ct);

    public async Task<DbSchemaStatusSnapshot> ReadSchemaStatusAsync(
        DbSchemaVersionContext schemaContext,
        DbMigrationTrigger trigger,
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

        if (ShouldApplyDbGuard(connectionOptions))
        {
            ApplyDbGuard(compatibility);
        }

        var migrationPolicy = await EvaluatePolicyAsync(
            trigger,
            compatibility.Status,
            schemaContext,
            userConfirmed: false,
            ciMigrationAuthorized: false,
            connectionOptions,
            ct).ConfigureAwait(false);

        var current = schema.Value ?? string.Empty;
        var updatable = (compatibility.IsTooLow || compatibility.IsMetadataMissing)
                        && (compatibility.IsMetadataMissing || IsSchemaUpdatable(current, localTarget))
                        && (migrationPolicy.RunMigration
                            || migrationPolicy.Decision == DbMigrationDecision.RequiresConfirmation);
        var snapshot = new DbSchemaStatusSnapshot(
            SchemaOk: schema.Ok,
            CurrentVersion: current,
            Reason: schema.Ok ? null : schema.Reason ?? "读取失败",
            TargetVersion: localTarget,
            RequiredMinVersion: requiredMin,
            RequiredMaxVersion: requiredMax,
            Compatibility: compatibility.Status,
            Satisfied: compatibility.IsCompatible,
            Updatable: updatable,
            ManualMigrationPolicy: migrationPolicy,
            IncompatibleMessage: null);
        return snapshot with
        {
            IncompatibleMessage = snapshot.Satisfied
                ? null
                : BuildIncompatibleMessage(schemaContext, snapshot)
        };
    }

    private async Task<DbMigrationPolicyResult> EvaluatePolicyAsync(
        DbMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        DbSchemaVersionContext schemaContext,
        bool userConfirmed,
        bool ciMigrationAuthorized,
        PgOptions? connectionOptions,
        CancellationToken ct)
    {
        var environment = connectionOptions is null
            ? await _envSettings.TryReadAsync(ct).ConfigureAwait(false)
            : await _envSettings.TryReadAsync(connectionOptions, ct).ConfigureAwait(false);
        return _migrationPolicy.Evaluate(new DbMigrationEvaluationContext(
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
        {
            return DbSchemaCompat.Evaluate(schema.Value, requiredMin, requiredMax);
        }

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
                {
                    machines.Add(machine);
                }
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
        {
            return false;
        }

        if (!DbSchemaCompat.TryParseSemVer(localTargetVersion ?? string.Empty, out var target))
        {
            return false;
        }

        return DbSchemaCompat.CompareSemVer(current, target) < 0;
    }

    private void ApplyDbGuard(DbSchemaCompatibilityResult compatibility)
    {
        if (compatibility.IsCompatible)
        {
            _accessGuard.Clear();
        }
        else
        {
            _accessGuard.Block(compatibility.Message);
        }
    }

    private bool ShouldApplyDbGuard(PgOptions? connectionOptions)
    {
        if (connectionOptions is null)
        {
            return true;
        }

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
        {
            return string.Empty;
        }

        var parsed = ClientParser.Parse(text);
        return (parsed.Machine ?? text).Trim();
    }
}
