using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class DatabaseMigrationPolicyServiceTests
{
    private readonly DatabaseMigrationPolicyService _service = new(new FixedEnvironmentSettingsService());

    [Fact]
    public void Stable_channel_with_stable_only_allows_in_app_migration_when_below_minimum()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.Allowed, result.Decision);
        Assert.True(result.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DatabaseMigrationTrigger.Startup)]
    [InlineData(DatabaseMigrationTrigger.Reconnect)]
    [InlineData(DatabaseMigrationTrigger.SettingsManual)]
    [InlineData(DatabaseMigrationTrigger.ExternalDeploy)]
    public void Stable_only_policy_is_consistent_across_all_migration_triggers(
        DatabaseMigrationTrigger trigger)
    {
        var result = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.Allowed, result.Decision);
        Assert.True(result.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DatabaseMigrationTrigger.Startup)]
    [InlineData(DatabaseMigrationTrigger.Reconnect)]
    [InlineData(DatabaseMigrationTrigger.SettingsManual)]
    [InlineData(DatabaseMigrationTrigger.ExternalDeploy)]
    public void Beta_channel_with_stable_only_blocks_every_migration_trigger(
        DatabaseMigrationTrigger trigger)
    {
        var result = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.False(result.ShouldExecuteMigration);
        Assert.Contains("Beta 应用禁止迁移", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_policy_blocks_in_app_migration_but_allows_external_deploy()
    {
        var inApp = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.Manual);
        var external = Evaluate(
            DatabaseMigrationTrigger.ExternalDeploy,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.Manual);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, inApp.Decision);
        Assert.Equal(DatabaseMigrationDecision.Allowed, external.Decision);
        Assert.True(external.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DatabaseMigrationTrigger.Startup)]
    [InlineData(DatabaseMigrationTrigger.Reconnect)]
    [InlineData(DatabaseMigrationTrigger.SettingsManual)]
    public void Manual_policy_blocks_every_in_app_migration_trigger(
        DatabaseMigrationTrigger trigger)
    {
        var result = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.Manual);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.False(result.ShouldExecuteMigration);
    }

    [Fact]
    public void Isolated_beta_without_authorization_is_blocked()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.SettingsManual,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment: DatabaseEnvironmentSettings.ProductionDefaults);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("Database.Environment=isolated", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Isolated_beta_requires_confirmation_before_manual_migration()
    {
        var pending = Evaluate(
            DatabaseMigrationTrigger.SettingsManual,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment: new DatabaseEnvironmentSettings("isolated", true));

        var allowed = Evaluate(
            DatabaseMigrationTrigger.SettingsManual,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment: new DatabaseEnvironmentSettings("isolated", true),
            userConfirmed: true);

        Assert.Equal(DatabaseMigrationDecision.RequiresConfirmation, pending.Decision);
        Assert.False(pending.ShouldExecuteMigration);
        Assert.Equal(DatabaseMigrationDecision.Allowed, allowed.Decision);
        Assert.True(allowed.ShouldExecuteMigration);
    }

    [Theory]
    [InlineData(DatabaseMigrationTrigger.Startup)]
    [InlineData(DatabaseMigrationTrigger.Reconnect)]
    [InlineData(DatabaseMigrationTrigger.SettingsManual)]
    public void Isolated_beta_requires_user_confirmation_for_every_in_app_trigger(
        DatabaseMigrationTrigger trigger)
    {
        var environment = new DatabaseEnvironmentSettings("isolated", true);
        var pending = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment);
        var allowed = Evaluate(
            trigger,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment,
            userConfirmed: true);

        Assert.Equal(DatabaseMigrationDecision.RequiresConfirmation, pending.Decision);
        Assert.False(pending.ShouldExecuteMigration);
        Assert.Equal(DatabaseMigrationDecision.Allowed, allowed.Decision);
        Assert.True(allowed.ShouldExecuteMigration);
    }

    [Fact]
    public void Isolated_beta_external_deploy_requires_ci_authorization()
    {
        var blocked = Evaluate(
            DatabaseMigrationTrigger.ExternalDeploy,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment: new DatabaseEnvironmentSettings("isolated", true));

        var allowed = Evaluate(
            DatabaseMigrationTrigger.ExternalDeploy,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.IsolatedBeta,
            environment: new DatabaseEnvironmentSettings("isolated", true),
            ciMigrationAuthorized: true);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, blocked.Decision);
        Assert.Equal(DatabaseMigrationDecision.Allowed, allowed.Decision);
    }

    [Fact]
    public void Unknown_migration_policy_is_fail_closed()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "stable",
            "typo-policy");

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("未知 migrationPolicy", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_release_channel_is_fail_closed()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.BelowMinimum,
            releaseChannel: "nightly",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("未知发布通道", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_missing_on_stable_channel_allows_in_app_migration()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.MetadataMissing,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.Allowed, result.Decision);
        Assert.True(result.ShouldExecuteMigration);
    }

    [Fact]
    public void Metadata_missing_on_beta_channel_is_blocked()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.MetadataMissing,
            releaseChannel: "beta",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("Beta 应用禁止迁移", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_schema_read_failure_is_blocked()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.Unknown,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.Contains("无法读取数据库版本", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Above_maximum_always_requires_read_only_mode()
    {
        var result = Evaluate(
            DatabaseMigrationTrigger.Startup,
            DbSchemaCompatibility.AboveMaximum,
            releaseChannel: "stable",
            DatabaseMigrationPolicies.StableOnly);

        Assert.Equal(DatabaseMigrationDecision.ReadOnlyRequired, result.Decision);
        Assert.False(result.ShouldExecuteMigration);
    }

    private DatabaseMigrationPolicyResult Evaluate(
        DatabaseMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        string releaseChannel,
        string migrationPolicy,
        DatabaseEnvironmentSettings? environment = null,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false)
        => _service.Evaluate(new DatabaseMigrationEvaluationContext(
            trigger,
            compatibility,
            releaseChannel,
            migrationPolicy,
            environment ?? DatabaseEnvironmentSettings.ProductionDefaults,
            userConfirmed,
            ciMigrationAuthorized));

    private sealed class FixedEnvironmentSettingsService : Application.Abstractions.IDatabaseEnvironmentSettingsService
    {
        public Task<DatabaseEnvironmentSettings> TryReadAsync(CancellationToken ct)
            => Task.FromResult(DatabaseEnvironmentSettings.ProductionDefaults);

        public Task<DatabaseEnvironmentSettings> TryReadAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(DatabaseEnvironmentSettings.ProductionDefaults);
    }
}
