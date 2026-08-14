namespace PacToolkits.Application.DTOs;

/// <summary>
/// 模块协议依赖（与 module.json 的 min/maxApiContract 同形；完整成对才算依赖 PacAPI 协议）
/// </summary>
public sealed record AgentsModuleBound(
    string ModuleId,
    string? MinApiContract,
    string? MaxApiContract)
{
    public bool RequiresApiContract
        => !string.IsNullOrWhiteSpace(MinApiContract) && !string.IsNullOrWhiteSpace(MaxApiContract);
}

/// <summary>挂载门禁拒绝原因（仅策略侧，不涉及 Host 进程）</summary>
public enum AgentsAdmitDenyKind
{
    None = 0,
    InvalidModule,
    Disconnected,
    ContractUnread,
    ContractOutOfRange,
}

/// <summary>单模块是否允许写入 desired</summary>
public sealed record AgentsAdmitResult(
    string ModuleId,
    bool Ok,
    AgentsAdmitDenyKind DenyKind,
    string Message);
