using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 挂载门禁：连库且当前 schema ∈ 模块区间
///
/// 产出可写 desired 的判定；不启停模块进程，也不操作 Host
/// </summary>
public interface IAgentsAdmitService
{
    Task<AgentsAdmitResult> AdmitAsync(
        AgentsModuleDbBound module,
        bool databaseConnected,
        CancellationToken ct = default);

    /// <summary>批量判定；库已连时只读一次 schema</summary>
    Task<IReadOnlyList<AgentsAdmitResult>> AdmitManyAsync(
        IReadOnlyList<AgentsModuleDbBound> modules,
        bool databaseConnected,
        CancellationToken ct = default);
}
