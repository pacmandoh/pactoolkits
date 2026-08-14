using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Validation;

/// <summary>
/// Host 启停前校验配置 SchemaVersion
/// </summary>
public static class AgentsConfigValidator
{
    public sealed record LaunchContext(int SchemaVersion);

    public static AgentsCommandResult ValidateForLaunch(LaunchContext context)
    {
        if (context.SchemaVersion != 2)
        {
            return new AgentsCommandResult(
                false,
                $"配置版本不受支持：{context.SchemaVersion}（仅支持 SchemaVersion=2）");
        }

        return new AgentsCommandResult(true, "ok");
    }
}
