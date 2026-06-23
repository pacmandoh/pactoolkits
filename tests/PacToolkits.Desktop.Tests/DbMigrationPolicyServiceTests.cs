using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class DbMigrationPolicyServiceTests
{
    private readonly DbMigrationPolicyService _service = new(new FixedDbEnvSettingsService());

    [Fact]
    public void Stable_channel_with_stable_only_allows_in_app_migration_when_below_minimum()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.Allowed, result.Decision);
        Assert.True(result.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DbMigrationTrigger.Startup)]
    [InlineData(DbMigrationTrigger.Reconnect)]
    [InlineData(DbMigrationTrigger.SettingsManual)]
    [InlineData(DbMigrationTrigger.ExternalDeploy)]
    public void Stable_only_policy_is_consistent_across_all_migration_triggers(
        DbMigrationTrigger trigger)
    {
        var result = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.Allowed, result.Decision);
        Assert.True(result.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DbMigrationTrigger.Startup)]
    [InlineData(DbMigrationTrigger.Reconnect)]
    [InlineData(DbMigrationTrigger.SettingsManual)]
    [InlineData(DbMigrationTrigger.ExternalDeploy)]
    public void Beta_channel_with_stable_only_blocks_every_migration_trigger(
        DbMigrationTrigger trigger)
    {
        var result = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.False(result.ShouldExecuteMigration);
        Assert.Contains("Beta 应用禁止迁移", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_policy_blocks_in_app_migration_but_allows_external_deploy()
    {
        var inApp = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DbMigrationPolicies.Manual);
        var external = Evaluate(
            DbMigrationTrigger.ExternalDeploy,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DbMigrationPolicies.Manual);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, inApp.Decision);
        Assert.Equal(DbMigrationDecision.Allowed, external.Decision);
        Assert.True(external.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DbMigrationTrigger.Startup)]
    [InlineData(DbMigrationTrigger.Reconnect)]
    [InlineData(DbMigrationTrigger.SettingsManual)]
    public void Manual_policy_blocks_every_in_app_migration_trigger(
        DbMigrationTrigger trigger)
    {
        var result = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DbMigrationPolicies.Manual);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.False(result.ShouldExecuteMigration);
    }

    [Fact]
    public void Isolated_beta_without_authorization_is_blocked()
    {
        var result = Evaluate(
            DbMigrationTrigger.SettingsManual,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment: DbEnvSettings.ProductionDefaults);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("Database.Environment=isolated", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Isolated_beta_requires_confirmation_before_manual_migration()
    {
        var pending = Evaluate(
            DbMigrationTrigger.SettingsManual,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment: new DbEnvSettings("isolated", true));

        var allowed = Evaluate(
            DbMigrationTrigger.SettingsManual,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment: new DbEnvSettings("isolated", true),
            userConfirmed: true);

        Assert.Equal(DbMigrationDecision.RequiresConfirmation, pending.Decision);
        Assert.False(pending.ShouldExecuteMigration);
        Assert.Equal(DbMigrationDecision.Allowed, allowed.Decision);
        Assert.True(allowed.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DbMigrationTrigger.Startup)]
    [InlineData(DbMigrationTrigger.Reconnect)]
    [InlineData(DbMigrationTrigger.SettingsManual)]
    public void Isolated_beta_requires_user_confirmation_for_every_in_app_trigger(
        DbMigrationTrigger trigger)
    {
        var environment = new DbEnvSettings("isolated", true);
        var pending = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment);
        var allowed = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment,
            userConfirmed: true);

        Assert.Equal(DbMigrationDecision.RequiresConfirmation, pending.Decision);
        Assert.False(pending.ShouldExecuteMigration);
        Assert.Equal(DbMigrationDecision.Allowed, allowed.Decision);
        Assert.True(allowed.ShouldExecuteMigration);
    }

    [Fact]
    public void Isolated_beta_external_deploy_requires_ci_authorization()
    {
        var blocked = Evaluate(
            DbMigrationTrigger.ExternalDeploy,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment: new DbEnvSettings("isolated", true));

        var allowed = Evaluate(
            DbMigrationTrigger.ExternalDeploy,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DbMigrationPolicies.IsolatedBeta,
            environment: new DbEnvSettings("isolated", true),
            ciMigrationAuthorized: true);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, blocked.Decision);
        Assert.Equal(DbMigrationDecision.Allowed, allowed.Decision);
    }

    [Fact]
    public void Unknown_migration_policy_is_fail_closed()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            "typo-policy");

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("未知 migrationPolicy", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_release_channel_is_fail_closed()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "nightly",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("未知发布通道", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_missing_on_stable_channel_allows_in_app_migration()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.MetadataMissing,
            releaseChannel: "stable",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.Allowed, result.Decision);
        Assert.True(result.ShouldExecuteMigration);
    }

    [Fact]
    public void Metadata_missing_on_beta_channel_is_blocked()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.MetadataMissing,
            releaseChannel: "beta",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("Beta 应用禁止迁移", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_schema_read_failure_is_blocked()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.Unknown,
            releaseChannel: "stable",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("无法读取数据库版本", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Above_maximum_always_requires_read_only_mode()
    {
        var result = Evaluate(
            DbMigrationTrigger.Startup,
            DbSchemaCompatibility.AboveMaximum,
            releaseChannel: "stable",
            DbMigrationPolicies.StableOnly);

        Assert.Equal(DbMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.False(result.ShouldExecuteMigration);
    }

    private DbMigrationPolicyResult Evaluate(
        DbMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        string releaseChannel,
        string migrationPolicy,
        DbEnvSettings? environment = null,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false)
        => _service.Evaluate(new DbMigrationEvaluationContext(
            trigger,
            compatibility,
            releaseChannel,
            migrationPolicy,
            environment ?? DbEnvSettings.ProductionDefaults,
            userConfirmed,
            ciMigrationAuthorized));

    private sealed class FixedDbEnvSettingsService : Application.Abstractions.IDbEnvSettingsService
    {
        public Task<DbEnvSettings> TryReadAsync(CancellationToken ct)
            => Task.FromResult(DbEnvSettings.ProductionDefaults);

        public Task<DbEnvSettings> TryReadAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(DbEnvSettings.ProductionDefaults);
    }
}
