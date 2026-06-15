using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

public sealed class DatabaseMigrationPolicyService : IDatabaseMigrationPolicyService
{
    private readonly IDatabaseEnvironmentSettingsService _environmentSettings;

    public DatabaseMigrationPolicyService(IDatabaseEnvironmentSettingsService environmentSettings)
    {
        _environmentSettings = environmentSettings
            ?? throw new ArgumentNullException(nameof(environmentSettings));
    }

    public async Task<DatabaseMigrationPolicyResult> EvaluateAsync(
        DatabaseMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        string releaseChannel,
        string migrationPolicy,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default)
    {
        var environment = await _environmentSettings.TryReadAsync(ct).ConfigureAwait(false);
        return Evaluate(new DatabaseMigrationEvaluationContext(
            trigger,
            compatibility,
            releaseChannel,
            migrationPolicy,
            environment,
            userConfirmed,
            ciMigrationAuthorized));
    }

    public DatabaseMigrationPolicyResult Evaluate(DatabaseMigrationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Compatibility == DbSchemaCompatibility.AboveMaximum)
        {
            return Block(
                "数据库版本高于当前程序支持范围，已阻断迁移与写入操作");
        }

        if (context.Compatibility == DbSchemaCompatibility.Unknown)
        {
            return Block("无法读取数据库版本，已阻断迁移");
        }

        if (context.Compatibility == DbSchemaCompatibility.Compatible)
        {
            return new DatabaseMigrationPolicyResult(
                DatabaseMigrationDecision.Allowed,
                "数据库版本已在支持范围内",
                ShouldExecuteMigration: false);
        }

        if (!TryNormalizePolicy(context.MigrationPolicy, out var policy))
        {
            return Block($"未知 migrationPolicy: {context.MigrationPolicy}，已阻断迁移");
        }

        if (!TryNormalizeChannel(context.ReleaseChannel, out var channel))
        {
            return Block($"未知发布通道: {context.ReleaseChannel}，已阻断迁移");
        }

        var isBeta = channel == "beta";

        return policy switch
        {
            DatabaseMigrationPolicies.StableOnly when !isBeta
                => AllowInAppMigration(context.Trigger, "Stable 通道允许应用内迁移"),

            DatabaseMigrationPolicies.StableOnly when isBeta
                => Block("Beta 应用禁止迁移共享生产数据库。请使用隔离测试库或等待 Stable 发布。"),

            DatabaseMigrationPolicies.Manual when context.Trigger == DatabaseMigrationTrigger.ExternalDeploy
                => new DatabaseMigrationPolicyResult(
                    DatabaseMigrationDecision.Allowed,
                    "manual 策略仅允许外部手动部署迁移",
                    ShouldExecuteMigration: true),

            DatabaseMigrationPolicies.Manual
                => Block("当前迁移策略为 manual，仅允许外部手动部署"),

            DatabaseMigrationPolicies.IsolatedBeta when !isBeta
                => Block("isolated-beta 策略仅适用于 Beta 发布通道"),

            DatabaseMigrationPolicies.IsolatedBeta
                => EvaluateIsolatedBeta(context),

            _
                => Block($"不支持的 migrationPolicy: {context.MigrationPolicy}"),
        };
    }

    private static DatabaseMigrationPolicyResult EvaluateIsolatedBeta(DatabaseMigrationEvaluationContext context)
    {
        if (!context.EnvironmentSettings.IsIsolated)
        {
            return Block("Beta 数据库迁移需要 Database.Environment=isolated");
        }

        if (!context.EnvironmentSettings.AllowBetaMigrations)
        {
            return Block("Beta 数据库迁移未授权（Database.AllowBetaMigrations=false）");
        }

        if (context.Trigger == DatabaseMigrationTrigger.ExternalDeploy)
        {
            return context.CiMigrationAuthorized
                ? new DatabaseMigrationPolicyResult(
                    DatabaseMigrationDecision.Allowed,
                    "CI 已授权 Beta 隔离库迁移",
                    ShouldExecuteMigration: true)
                : Block("Beta 数据库迁移需要 CI 显式授权");
        }

        if (!context.UserConfirmed)
        {
            return new DatabaseMigrationPolicyResult(
                DatabaseMigrationDecision.RequiresConfirmation,
                "Beta 隔离库迁移需要二次确认",
                ShouldExecuteMigration: false);
        }

        return new DatabaseMigrationPolicyResult(
            DatabaseMigrationDecision.Allowed,
            "已满足 isolated-beta 迁移授权",
            ShouldExecuteMigration: true);
    }

    private static DatabaseMigrationPolicyResult AllowInAppMigration(
        DatabaseMigrationTrigger trigger,
        string reason)
    {
        _ = trigger;
        return new DatabaseMigrationPolicyResult(
            DatabaseMigrationDecision.Allowed,
            reason,
            ShouldExecuteMigration: true);
    }

    private static DatabaseMigrationPolicyResult Block(string reason)
        => new(DatabaseMigrationDecision.ReadOnlyRequired, reason, ShouldExecuteMigration: false);

    private static bool TryNormalizePolicy(string? migrationPolicy, out string policy)
    {
        var normalized = (migrationPolicy ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case DatabaseMigrationPolicies.StableOnly:
            case DatabaseMigrationPolicies.Manual:
            case DatabaseMigrationPolicies.IsolatedBeta:
                policy = normalized;
                return true;
            default:
                policy = normalized;
                return false;
        }
    }

    private static bool TryNormalizeChannel(string? releaseChannel, out string channel)
    {
        var normalized = (releaseChannel ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "stable" or "beta")
        {
            channel = normalized;
            return true;
        }

        channel = normalized;
        return false;
    }
}
