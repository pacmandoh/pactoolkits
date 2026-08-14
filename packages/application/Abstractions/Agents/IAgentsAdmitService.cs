using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 挂载门禁：PacAPI 就绪且当前 contractVersion 落在模块区间
///
/// 产出可写 desired 的判定；不启停模块进程，也不操作 Host
/// </summary>
public interface IAgentsAdmitService
{
    Task<AgentsAdmitResult> AdmitAsync(
        AgentsModuleBound module,
        bool apiReady,
        string? contractVersion = null,
        CancellationToken ct = default);

    /// <summary>批量判定；contractVersion 由探测传入</summary>
    Task<IReadOnlyList<AgentsAdmitResult>> AdmitManyAsync(
        IReadOnlyList<AgentsModuleBound> modules,
        bool apiReady,
        string? contractVersion = null,
        CancellationToken ct = default);
}
