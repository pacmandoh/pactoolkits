using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

public sealed class DbMigrationPolicyService : IDbMigrationPolicyService
{
    private readonly IDbEnvSettingsService _environmentSettings;

    public DbMigrationPolicyService(IDbEnvSettingsService environmentSettings)
    {
        _environmentSettings = environmentSettings
            ?? throw new ArgumentNullException(nameof(environmentSettings));
    }

    public async Task<DbMigrationPolicyResult> EvaluateAsync(
        DbMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        string releaseChannel,
        string migrationPolicy,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default)
    {
        var environment = await _environmentSettings.TryReadAsync(ct).ConfigureAwait(false);
        return Evaluate(new DbMigrationEvaluationContext(
            trigger,
            compatibility,
            releaseChannel,
            migrationPolicy,
            environment,
            userConfirmed,
            ciMigrationAuthorized));
    }

    public DbMigrationPolicyResult Evaluate(DbMigrationEvaluationContext context)
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
            return new DbMigrationPolicyResult(
                DbMigrationDecision.Allowed,
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
            DbMigrationPolicies.StableOnly when !isBeta
                => AllowInAppMigration(context.Trigger, "Stable 通道允许应用内迁移"),

            DbMigrationPolicies.StableOnly when isBeta
                => Block("Beta 应用禁止迁移共享生产数据库。请使用隔离测试库或等待 Stable 发布。"),

            DbMigrationPolicies.Manual when context.Trigger == DbMigrationTrigger.ExternalDeploy
                => new DbMigrationPolicyResult(
                    DbMigrationDecision.Allowed,
                    "manual 策略仅允许外部手动部署迁移",
                    ShouldExecuteMigration: true),

            DbMigrationPolicies.Manual
                => Block("当前迁移策略为 manual，仅允许外部手动部署"),

            DbMigrationPolicies.IsolatedBeta when !isBeta
                => Block("isolated-beta 策略仅适用于 Beta 发布通道"),

            DbMigrationPolicies.IsolatedBeta
                => EvaluateIsolatedBeta(context),

            _
                => Block($"不支持的 migrationPolicy: {context.MigrationPolicy}"),
        };
    }

    private static DbMigrationPolicyResult EvaluateIsolatedBeta(DbMigrationEvaluationContext context)
    {
        if (!context.EnvironmentSettings.IsIsolated)
        {
            return Block("Beta 数据库迁移需要 Database.Environment=isolated");
        }

        if (!context.EnvironmentSettings.AllowBetaMigrations)
        {
            return Block("Beta 数据库迁移未授权（Database.AllowBetaMigrations=false）");
        }

        if (context.Trigger == DbMigrationTrigger.ExternalDeploy)
        {
            return context.CiMigrationAuthorized
                ? new DbMigrationPolicyResult(
                    DbMigrationDecision.Allowed,
                    "CI 已授权 Beta 隔离库迁移",
                    ShouldExecuteMigration: true)
                : Block("Beta 数据库迁移需要 CI 显式授权");
        }

        if (!context.UserConfirmed)
        {
            return new DbMigrationPolicyResult(
                DbMigrationDecision.RequiresConfirmation,
                "Beta 隔离库迁移需要二次确认",
                ShouldExecuteMigration: false);
        }

        return new DbMigrationPolicyResult(
            DbMigrationDecision.Allowed,
            "已满足 isolated-beta 迁移授权",
            ShouldExecuteMigration: true);
    }

    private static DbMigrationPolicyResult AllowInAppMigration(
        DbMigrationTrigger trigger,
        string reason)
    {
        _ = trigger;
        return new DbMigrationPolicyResult(
            DbMigrationDecision.Allowed,
            reason,
            ShouldExecuteMigration: true);
    }

    private static DbMigrationPolicyResult Block(string reason)
        => new(DbMigrationDecision.ReadOnlyRequired, reason, ShouldExecuteMigration: false);

    private static bool TryNormalizePolicy(string? migrationPolicy, out string policy)
    {
        var normalized = (migrationPolicy ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case DbMigrationPolicies.StableOnly:
            case DbMigrationPolicies.Manual:
            case DbMigrationPolicies.IsolatedBeta:
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
