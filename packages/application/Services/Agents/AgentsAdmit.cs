using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>
/// 模块 desired 门禁：PacAPI 就绪且 contractVersion 落在模块 min/maxApiContract
/// </summary>
public sealed class AgentsAdmitService : IAgentsAdmitService
{
    public async Task<AgentsAdmitResult> AdmitAsync(
        AgentsModuleBound module,
        bool apiReady,
        string? contractVersion = null,
        CancellationToken ct = default)
    {
        var results = await AdmitManyAsync([module], apiReady, contractVersion, ct).ConfigureAwait(false);
        return results[0];
    }

    public Task<IReadOnlyList<AgentsAdmitResult>> AdmitManyAsync(
        IReadOnlyList<AgentsModuleBound> modules,
        bool apiReady,
        string? contractVersion = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modules);

        if (modules.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<AgentsAdmitResult>>(Array.Empty<AgentsAdmitResult>());
        }

        var results = new List<AgentsAdmitResult>(modules.Count);

        foreach (var module in modules)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(module.ModuleId))
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId ?? string.Empty,
                    Ok: false,
                    AgentsAdmitDenyKind.InvalidModule,
                    "模块 id 无效"));
                continue;
            }

            if (!module.RequiresApiContract)
            {
                results.Add(Ok(module.ModuleId));
                continue;
            }

            if (!apiReady)
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId,
                    Ok: false,
                    AgentsAdmitDenyKind.Disconnected,
                    $"PacAPI 未就绪，无法启动 {module.ModuleId}"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(contractVersion))
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId,
                    Ok: false,
                    AgentsAdmitDenyKind.ContractUnread,
                    $"无法读取 PacAPI 协议版本，无法启动 {module.ModuleId}"));
                continue;
            }

            var eval = SemVerRange.Classify(
                contractVersion,
                module.MinApiContract,
                module.MaxApiContract,
                allowPrerelease: false);
            if (!eval.IsCompatible)
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId,
                    Ok: false,
                    AgentsAdmitDenyKind.ContractOutOfRange,
                    RangeDeny(module.ModuleId, module.MinApiContract!, module.MaxApiContract!, eval)));
                continue;
            }

            results.Add(Ok(module.ModuleId));
        }

        return Task.FromResult<IReadOnlyList<AgentsAdmitResult>>(results);
    }

    private static AgentsAdmitResult Ok(string moduleId)
        => new(moduleId, Ok: true, AgentsAdmitDenyKind.None, "ok");

    private static string RangeDeny(
        string moduleId,
        string minApiContract,
        string maxApiContract,
        SemVerRangeResult eval)
        => eval.Status switch
        {
            SemVerRangeStatus.BelowMinimum
                => $"{moduleId} 需要协议 {minApiContract} - {maxApiContract}：当前 {eval.Current} 过低",
            SemVerRangeStatus.AboveMaximum
                => $"{moduleId} 需要协议 {minApiContract} - {maxApiContract}：当前 {eval.Current} 过高",
            _ => $"{moduleId} 需要协议 {minApiContract} - {maxApiContract}：当前 {eval.Current} 不兼容",
        };
}
