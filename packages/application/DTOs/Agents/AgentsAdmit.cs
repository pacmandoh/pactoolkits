namespace PacToolkits.Application.DTOs;

/// <summary>
/// 模块库依赖声明（与 module.json 的 min/maxDbSchema 同形；完整成对才算依赖库）
/// </summary>
public sealed record AgentsModuleDbBound(
    string ModuleId,
    string? MinDbSchema,
    string? MaxDbSchema)
{
    public bool RequiresDatabase
        => !string.IsNullOrWhiteSpace(MinDbSchema) && !string.IsNullOrWhiteSpace(MaxDbSchema);
}

/// <summary>挂载门禁拒绝原因（仅策略侧，不涉及 Host 进程）</summary>
public enum AgentsAdmitDenyKind
{
    None = 0,
    InvalidModule,
    Disconnected,
    SchemaUnread,
    SchemaOutOfRange,
}

/// <summary>单模块是否允许写入 desired</summary>
public sealed record AgentsAdmitResult(
    string ModuleId,
    bool Ok,
    AgentsAdmitDenyKind DenyKind,
    string Message);
