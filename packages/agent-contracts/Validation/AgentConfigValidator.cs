using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Validation;

public static class AgentConfigValidator
{
    public sealed record LaunchContext(
        int SchemaVersion,
        string PostgresHost,
        int PostgresPort,
        string PostgresDatabase,
        string PostgresUsername,
        AutomationToolsOptions AutomationTools);

    public static ToolCommandResult ValidateForLaunch(LaunchContext context)
    {
        if (context.SchemaVersion != 1)
        {
            return new ToolCommandResult(
                false,
                $"配置版本不受支持：{context.SchemaVersion}（仅支持 schemaVersion=1）");
        }

        if (string.IsNullOrWhiteSpace(context.PostgresHost)
            || context.PostgresPort <= 0
            || string.IsNullOrWhiteSpace(context.PostgresDatabase)
            || string.IsNullOrWhiteSpace(context.PostgresUsername))
        {
            return new ToolCommandResult(false, "统一配置校验失败：Postgres 关键字段不完整");
        }

        return ValidateAgentSection(context.AutomationTools.Agent);
    }

    public static ToolCommandResult ValidateAgentSection(AgentToolOptions agent)
    {
        if (string.IsNullOrWhiteSpace(agent.PgDriver)
            || string.IsNullOrWhiteSpace(agent.OptWindowClass)
            || string.IsNullOrWhiteSpace(agent.IptWindowClass)
            || string.IsNullOrWhiteSpace(agent.OptParseGridClassNN)
            || string.IsNullOrWhiteSpace(agent.OptVerifyGridClassNN)
            || string.IsNullOrWhiteSpace(agent.IptParseGridClassNN)
            || string.IsNullOrWhiteSpace(agent.IptVerifyGridClassNN)
            || string.IsNullOrWhiteSpace(agent.OptInputClassNN)
            || string.IsNullOrWhiteSpace(agent.IptInputClassNN))
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent 文本字段不完整");
        }

        if (!IsSupportedPgSslMode(agent.PgSsl))
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.PgSsl 仅支持 disable/allow/prefer/require/verify-ca/verify-full");
        }

        if (agent.AppWin is null || agent.AppWin.Count == 0)
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.AppWin 不能为空");
        }

        if (agent.ColSpecs is null || agent.ColSpecs.Count == 0)
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.ColSpecs 不能为空");
        }

        if (agent.ConfirmTimeoutMs < 100 || agent.ConfirmTimeoutMs > 10000)
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.ConfirmTimeoutMs 超出范围（100-10000）");
        }

        if (!string.Equals(agent.CodePickPolicy, "MAX_LEVEL", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(agent.CodePickPolicy, "MIN_LEVEL", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.CodePickPolicy 仅支持 MAX_LEVEL/MIN_LEVEL");
        }

        if (string.IsNullOrWhiteSpace(agent.WarehouseTaskIdentifier))
        {
            return new ToolCommandResult(false, "统一配置校验失败：AutomationTools.Agent.WarehouseTaskIdentifier 不能为空");
        }

        return new ToolCommandResult(true, "ok");
    }

    private static bool IsSupportedPgSslMode(string? value)
    {
        var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
        return mode is "disable" or "allow" or "prefer" or "require" or "verify-ca" or "verify-full";
    }
}
