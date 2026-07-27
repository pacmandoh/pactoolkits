using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Validation;

/// <summary>
/// Agents Host 启动前配置校验（SchemaVersion、Postgres；模块业务走 ModuleSettingsValidator）
/// </summary>
public static class AgentsConfigValidator
{
    public sealed record LaunchContext(
        int SchemaVersion,
        string PostgresHost,
        int PostgresPort,
        string PostgresDatabase,
        string PostgresUsername,
        string PostgresPassword);

    /// <summary>Host 启动：SchemaVersion + Postgres（不含模块业务 settings）</summary>
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

        // 模块拒绝空 PG_PASS；Desktop 在 trust/空密码下仍可能连上，故此处单独拦住
        if (string.IsNullOrWhiteSpace(context.PostgresPassword))
        {
            return new AgentsCommandResult(
                false,
                "统一配置校验失败：Postgres.Password 为空（请先在设置中保存数据库密码后再启动 Agents）");
        }

        return new AgentsCommandResult(true, "ok");
    }
}
