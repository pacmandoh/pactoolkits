using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Agents.Contracts.Models;

namespace PacToolkits.Agents.Contracts.Validation;

/// <summary>
/// Agents 启动前统一配置校验（SchemaVersion=2、Postgres、Injector 字段）
/// </summary>
public static class AgentsConfigValidator
{
    public sealed record LaunchContext(
        int SchemaVersion,
        string PostgresHost,
        int PostgresPort,
        string PostgresDatabase,
        string PostgresUsername,
        string PostgresPassword,
        AgentsOptions Agents);

    public static AgentsCommandResult ValidateForLaunch(LaunchContext context)
    {
        if (context.SchemaVersion != 2)
        {
            return new AgentsCommandResult(
                false,
                $"配置版本不受支持：{context.SchemaVersion}（仅支持 SchemaVersion=2）");
        }

        if (string.IsNullOrWhiteSpace(context.PostgresHost)
            || context.PostgresPort <= 0
            || string.IsNullOrWhiteSpace(context.PostgresDatabase)
            || string.IsNullOrWhiteSpace(context.PostgresUsername))
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Postgres 连接字段不完整");
        }

        // Injector 拒绝空 PG_PASS；Desktop 在 trust/空密码下仍可能连上，故此处单独拦住
        if (string.IsNullOrWhiteSpace(context.PostgresPassword))
        {
            return new AgentsCommandResult(
                false,
                "统一配置校验失败：Postgres.Password 为空（请先在设置中保存数据库密码后再启动 Agents）");
        }

        return ValidateInjector(context.Agents.Injector);
    }

    private static AgentsCommandResult ValidateInjector(InjectorOptions injector)
    {
        if (string.IsNullOrWhiteSpace(injector.PgDriver)
            || string.IsNullOrWhiteSpace(injector.OptWindowClass)
            || string.IsNullOrWhiteSpace(injector.IptWindowClass)
            || string.IsNullOrWhiteSpace(injector.OptParseGridClassNN)
            || string.IsNullOrWhiteSpace(injector.OptVerifyGridClassNN)
            || string.IsNullOrWhiteSpace(injector.IptParseGridClassNN)
            || string.IsNullOrWhiteSpace(injector.IptVerifyGridClassNN)
            || string.IsNullOrWhiteSpace(injector.OptInputClassNN)
            || string.IsNullOrWhiteSpace(injector.IptInputClassNN))
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector 文本字段不完整");
        }

        if (!IsSupportedPgSslMode(injector.PgSsl))
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector.PgSsl 仅支持 disable/allow/prefer/require/verify-ca/verify-full");
        }

        if (injector.AppWin is null || injector.AppWin.Count == 0)
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector.AppWin 不能为空");
        }

        if (injector.ColSpecs is null || injector.ColSpecs.Count == 0)
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector.ColSpecs 不能为空");
        }

        if (injector.ConfirmTimeoutMs < 100 || injector.ConfirmTimeoutMs > 10000)
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector.ConfirmTimeoutMs 超出范围（100-10000）");
        }

        if (!string.Equals(injector.CodePickPolicy, "MAX_LEVEL", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(injector.CodePickPolicy, "MIN_LEVEL", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector.CodePickPolicy 仅支持 MAX_LEVEL/MIN_LEVEL");
        }

        if (string.IsNullOrWhiteSpace(injector.WarehouseTaskIdentifier))
        {
            return new AgentsCommandResult(false, "统一配置校验失败：Agents.Injector.WarehouseTaskIdentifier 不能为空");
        }

        return new AgentsCommandResult(true, "ok");
    }

    private static bool IsSupportedPgSslMode(string? value)
    {
        var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
        return mode is "disable" or "allow" or "prefer" or "require" or "verify-ca" or "verify-full";
    }
}
