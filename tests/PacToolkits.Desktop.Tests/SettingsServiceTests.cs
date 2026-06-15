using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task Validate_connection_reports_schema_incompatibility_for_beta_below_minimum()
    {
        var migration = new FakeMigrationService();
        var service = CreateService(migration, new DatabaseAccessGuard(), schemaVersion: "1.2.20");

        var result = await service.ValidateDatabaseConnectionAsync(
            new PgOptions(),
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.21",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.21",
                AgentMaxDbSchema: "1.2.22",
                TargetDbSchemaVersion: "1.2.22",
                ReleaseChannel: "beta",
                MigrationPolicy: DatabaseMigrationPolicies.StableOnly),
            CancellationToken.None);

        Assert.True(result.ConnectionOk);
        Assert.True(result.SchemaMigrationOk);
        Assert.False(result.SchemaCompatible);
        Assert.Equal(0, migration.CallCount);
        Assert.Contains("不兼容", result.IncompatibleMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta 应用禁止迁移", result.IncompatibleMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_candidate_connection_skips_migration_without_blocking_current_database()
    {
        var migration = new FakeMigrationService();
        var guard = new DatabaseAccessGuard();
        var service = CreateService(migration, guard, schemaVersion: "1.2.23");

        var result = await service.ValidateDatabaseConnectionAsync(
            new PgOptions(),
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.20",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.20",
                AgentMaxDbSchema: "1.2.25",
                TargetDbSchemaVersion: "1.2.22"),
            CancellationToken.None);

        Assert.True(result.ConnectionOk);
        Assert.False(result.SchemaCompatible);
        Assert.Equal(0, migration.CallCount);
        Assert.False(guard.IsBlocked);
        Assert.Contains(
            "数据库版本高于当前程序支持范围",
            result.IncompatibleMessage ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_current_connection_status_blocks_database_when_schema_is_above_maximum()
    {
        var migration = new FakeMigrationService();
        var guard = new DatabaseAccessGuard();
        var service = CreateService(migration, guard, schemaVersion: "1.2.23");

        var snapshot = await service.ReadSchemaStatusAsync(
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.20",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.20",
                AgentMaxDbSchema: "1.2.25",
                TargetDbSchemaVersion: "1.2.22"),
            CancellationToken.None);

        Assert.Equal(DbSchemaCompatibility.AboveMaximum, snapshot.Compatibility);
        Assert.True(guard.IsBlocked);
    }

    [Fact]
    public async Task Ensure_schema_up_to_date_blocks_beta_channel_migration()
    {
        var migration = new FakeMigrationService();
        var service = CreateService(migration, new DatabaseAccessGuard(), schemaVersion: "1.2.20");

        var result = await service.EnsureSchemaUpToDateAsync(
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.21",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.21",
                AgentMaxDbSchema: "1.2.22",
                TargetDbSchemaVersion: "1.2.22",
                ReleaseChannel: "beta",
                MigrationPolicy: DatabaseMigrationPolicies.StableOnly),
            DatabaseMigrationTrigger.SettingsManual,
            userConfirmed: false,
            ciMigrationAuthorized: false,
            ct: CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(0, migration.CallCount);
        Assert.Contains("Beta 应用禁止迁移", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_connection_uses_explicit_options_not_current_config()
    {
        var migration = new FakeMigrationService();
        var schemaService = new TrackingSchemaVersionService();
        var environmentService = new TrackingEnvironmentSettingsService();
        var currentConfig = new FakeDbConfigService();
        var explicitOptions = new PgOptions { Host = "explicit-host", Database = "explicit-db" };
        var service = new SettingsService(
            currentConfig,
            new FakeConnectionTester(),
            schemaService,
            migration,
            new FakeClientIdReadRepo(),
            new DatabaseAccessGuard(),
            new DatabaseMigrationPolicyService(environmentService),
            environmentService);

        await service.ValidateDatabaseConnectionAsync(
            explicitOptions,
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.20",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.20",
                AgentMaxDbSchema: "1.2.22",
                TargetDbSchemaVersion: "1.2.22"),
            CancellationToken.None);

        Assert.Equal(explicitOptions.Host, schemaService.LastOptions?.Host);
        Assert.Equal(explicitOptions.Database, schemaService.LastOptions?.Database);
        Assert.Equal(explicitOptions.Host, environmentService.LastOptions?.Host);
        Assert.Equal(explicitOptions.Database, environmentService.LastOptions?.Database);
        Assert.Equal(explicitOptions.Host, migration.LastOptions?.Host);
        Assert.Equal(explicitOptions.Database, migration.LastOptions?.Database);
        Assert.NotEqual(explicitOptions.Host, currentConfig.Current.Host);
    }

    [Fact]
    public async Task Migration_plan_uses_explicit_options_without_executing_migration()
    {
        var migration = new FakeMigrationService();
        var service = CreateService(migration, new DatabaseAccessGuard());
        var explicitOptions = new PgOptions
        {
            Host = "beta-db",
            Database = "pactoolkits_beta",
        };

        var plan = await service.GetSchemaMigrationPlanAsync(
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.20",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.20",
                AgentMaxDbSchema: "1.2.22",
                TargetDbSchemaVersion: "1.2.22",
                ReleaseChannel: "beta",
                MigrationPolicy: DatabaseMigrationPolicies.StableOnly),
            explicitOptions,
            CancellationToken.None);

        Assert.Equal(2, plan.PendingCount);
        Assert.Equal(0, migration.CallCount);
        Assert.Equal(explicitOptions.Host, migration.LastOptions?.Host);
        Assert.Equal(explicitOptions.Database, migration.LastOptions?.Database);
    }

    [Fact]
    public async Task Ensure_schema_up_to_date_allows_bootstrap_when_metadata_missing_on_stable()
    {
        var migration = new FakeMigrationService();
        var service = CreateService(
            migration,
            new DatabaseAccessGuard(),
            schemaReadResult: new DbSchemaVersionReadResult(
                false,
                null,
                "schema_version 表不存在",
                IsMetadataMissing: true));

        var result = await service.EnsureSchemaUpToDateAsync(
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.20",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.20",
                AgentMaxDbSchema: "1.2.22",
                TargetDbSchemaVersion: "1.2.22",
                ReleaseChannel: "stable",
                MigrationPolicy: DatabaseMigrationPolicies.StableOnly),
            DatabaseMigrationTrigger.Startup,
            userConfirmed: false,
            ciMigrationAuthorized: false,
            ct: CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(1, migration.CallCount);
    }

    [Fact]
    public async Task Metadata_missing_status_allows_stable_manual_bootstrap()
    {
        var service = CreateService(
            new FakeMigrationService(),
            new DatabaseAccessGuard(),
            schemaReadResult: new DbSchemaVersionReadResult(
                false,
                null,
                "schema_version 表不存在",
                IsMetadataMissing: true));

        var snapshot = await service.ReadSchemaStatusAsync(
            new DbSchemaVersionContext(
                UiMinDbSchema: "1.2.20",
                UiMaxDbSchema: "1.2.22",
                AgentMinDbSchema: "1.2.20",
                AgentMaxDbSchema: "1.2.22",
                TargetDbSchemaVersion: "1.2.22",
                ReleaseChannel: "stable",
                MigrationPolicy: DatabaseMigrationPolicies.StableOnly),
            new PgOptions(),
            CancellationToken.None);

        Assert.Equal(DbSchemaCompatibility.MetadataMissing, snapshot.Compatibility);
        Assert.True(snapshot.Updatable);
        Assert.Equal(DatabaseMigrationDecision.Allowed, snapshot.ManualMigrationPolicy.Decision);
        Assert.True(snapshot.ManualMigrationPolicy.ShouldExecuteMigration);
    }

    private static SettingsService CreateService(
        FakeMigrationService migration,
        DatabaseAccessGuard guard,
        string schemaVersion = "1.2.20",
        DbSchemaVersionReadResult? schemaReadResult = null)
        => new(
            new FakeDbConfigService(),
            new FakeConnectionTester(),
            new FakeSchemaVersionService(schemaReadResult ?? new DbSchemaVersionReadResult(true, schemaVersion, null)),
            migration,
            new FakeClientIdReadRepo(),
            guard,
            new DatabaseMigrationPolicyService(new FakeEnvironmentSettingsService()),
            new FakeEnvironmentSettingsService());

    private sealed class FakeEnvironmentSettingsService : IDatabaseEnvironmentSettingsService
    {
        public Task<DatabaseEnvironmentSettings> TryReadAsync(CancellationToken ct)
            => Task.FromResult(DatabaseEnvironmentSettings.ProductionDefaults);

        public Task<DatabaseEnvironmentSettings> TryReadAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(DatabaseEnvironmentSettings.ProductionDefaults);
    }

    private sealed class TrackingEnvironmentSettingsService : IDatabaseEnvironmentSettingsService
    {
        public PgOptions? LastOptions { get; private set; }

        public Task<DatabaseEnvironmentSettings> TryReadAsync(CancellationToken ct)
            => Task.FromResult(DatabaseEnvironmentSettings.ProductionDefaults);

        public Task<DatabaseEnvironmentSettings> TryReadAsync(PgOptions options, CancellationToken ct)
        {
            LastOptions = options;
            return Task.FromResult(DatabaseEnvironmentSettings.ProductionDefaults);
        }
    }

    private sealed class FakeDbConfigService : IDbConfigService
    {
        public PgOptions Current { get; } = new() { Host = "current-host", Database = "current-db" };
        public string ConfigPath => "/tmp/pactoolkits-test.config.json";
        public event EventHandler? Applied;

        public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(true);

        public Task SaveAndApplyAsync(PgOptions opt, CancellationToken ct = default)
        {
            Applied?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnectionTester : IDbConnectionTester
    {
        public Task<DbTestResult> TestAsync(PgOptions opt, CancellationToken ct = default)
            => Task.FromResult(new DbTestResult(true, "ok"));
    }

    private sealed class FakeSchemaVersionService(DbSchemaVersionReadResult result) : IDbSchemaVersionService
    {
        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(result);

        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(result);
    }

    private sealed class TrackingSchemaVersionService : IDbSchemaVersionService
    {
        public PgOptions? LastOptions { get; private set; }

        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionReadResult(true, "1.2.19", null));

        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
        {
            LastOptions = options;
            return Task.FromResult(new DbSchemaVersionReadResult(true, "1.2.19", null));
        }
    }

    private sealed class FakeMigrationService : IDbSchemaMigrationService
    {
        public int CallCount { get; private set; }
        public PgOptions? LastOptions { get; private set; }

        public Task<DbSchemaMigrationPlan> GetPlanAsync(
            CancellationToken ct,
            string? targetVersion = null)
            => Task.FromResult(CreatePlan(targetVersion));

        public Task<DbSchemaMigrationPlan> GetPlanAsync(
            PgOptions options,
            CancellationToken ct,
            string? targetVersion = null)
        {
            LastOptions = options;
            return Task.FromResult(CreatePlan(targetVersion));
        }

        public Task<DbSchemaMigrationResult> EnsureUpToDateAsync(
            CancellationToken ct,
            string? targetVersion = null)
        {
            CallCount++;
            return Task.FromResult(new DbSchemaMigrationResult(
                BeforeVersion: "1.2.20",
                AfterVersion: targetVersion,
                AppliedCount: 1,
                SkippedCount: 0));
        }

        public Task<DbSchemaMigrationResult> EnsureUpToDateAsync(
            PgOptions options,
            CancellationToken ct,
            string? targetVersion = null)
        {
            CallCount++;
            LastOptions = options;
            return Task.FromResult(new DbSchemaMigrationResult(
                BeforeVersion: null,
                AfterVersion: targetVersion,
                AppliedCount: 1,
                SkippedCount: 0));
        }

        private static DbSchemaMigrationPlan CreatePlan(string? targetVersion)
            => new(
                CurrentVersion: "1.2.20",
                TargetVersion: targetVersion ?? "1.2.22",
                BootstrapRequired: false,
                Items:
                [
                    new DbSchemaMigrationPlanItem("1.2.21", "V1_2_21__test.sql", Applied: false),
                    new DbSchemaMigrationPlanItem("1.2.22", "V1_2_22__test.sql", Applied: false),
                ]);
    }

    private sealed class FakeClientIdReadRepo : IClientIdReadRepo
    {
        public Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(new HashSet<string>());
    }
}
