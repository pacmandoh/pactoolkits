using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Validation;

/// <summary>
/// 校验 Host 启动所需的 Desktop 配置和数据库连接参数，不校验模块业务配置
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

        // Injector 不支持依赖 PostgreSQL trust 的空密码连接，Host 启动前必须单独约束
        if (string.IsNullOrWhiteSpace(context.PostgresPassword))
        {
            return new AgentsCommandResult(
                false,
                "统一配置校验失败：Postgres.Password 为空（请先在设置中保存数据库密码后再启动 Agents）");
        }

        return new AgentsCommandResult(true, "ok");
    }
}
