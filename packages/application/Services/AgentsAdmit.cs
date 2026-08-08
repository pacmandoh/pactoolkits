using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>
/// 模块 desired 门禁：已连且 <see cref="IDbSchemaGate"/> 区间 Match
/// </summary>
public sealed class AgentsAdmitService : IAgentsAdmitService
{
    private readonly IDbSchemaGate _schemaGate;

    public AgentsAdmitService(IDbSchemaGate schemaGate)
    {
        _schemaGate = schemaGate ?? throw new ArgumentNullException(nameof(schemaGate));
    }

    public async Task<AgentsAdmitResult> AdmitAsync(
        AgentsModuleDbBound module,
        bool databaseConnected,
        CancellationToken ct = default)
    {
        var list = await AdmitManyAsync([module], databaseConnected, ct).ConfigureAwait(false);
        return list[0];
    }

    public async Task<IReadOnlyList<AgentsAdmitResult>> AdmitManyAsync(
        IReadOnlyList<AgentsModuleDbBound> modules,
        bool databaseConnected,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modules);

        if (modules.Count == 0)
        {
            return Array.Empty<AgentsAdmitResult>();
        }

        DbSchemaVersionRead? schemaRead = null;
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

            if (!module.RequiresDatabase)
            {
                results.Add(Ok(module.ModuleId));
                continue;
            }

            if (!databaseConnected)
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId,
                    Ok: false,
                    AgentsAdmitDenyKind.Disconnected,
                    $"数据库未连接，无法启动 {module.ModuleId}"));
                continue;
            }

            schemaRead ??= await _schemaGate.ReadAsync(ct).ConfigureAwait(false);
            if (!schemaRead.Ok || string.IsNullOrWhiteSpace(schemaRead.Value))
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId,
                    Ok: false,
                    AgentsAdmitDenyKind.SchemaUnread,
                    $"无法读取数据库版本，无法启动 {module.ModuleId}：{schemaRead.Reason ?? "读取失败"}"));
                continue;
            }

            var eval = _schemaGate.Match(schemaRead, module.MinDbSchema!, module.MaxDbSchema!);
            if (!eval.IsCompatible)
            {
                results.Add(new AgentsAdmitResult(
                    module.ModuleId,
                    Ok: false,
                    AgentsAdmitDenyKind.SchemaOutOfRange,
                    RangeDeny(module.ModuleId, module.MinDbSchema!, module.MaxDbSchema!, eval)));
                continue;
            }

            results.Add(Ok(module.ModuleId));
        }

        return results;
    }

    private static AgentsAdmitResult Ok(string moduleId)
        => new(moduleId, Ok: true, AgentsAdmitDenyKind.None, "ok");

    // 模块侧短句；业务长文走 DbSchemaDesktop
    private static string RangeDeny(
        string moduleId,
        string minDbSchema,
        string maxDbSchema,
        DbSchemaCompatibilityResult eval)
        => eval.Status switch
        {
            DbSchemaCompatibility.BelowMinimum
                => $"{moduleId} 需要数据库 {minDbSchema} - {maxDbSchema}：当前 {eval.CurrentVersion} 过低",
            DbSchemaCompatibility.AboveMaximum
                => $"{moduleId} 需要数据库 {minDbSchema} - {maxDbSchema}：当前 {eval.CurrentVersion} 过高",
            _ => $"{moduleId} 需要数据库 {minDbSchema} - {maxDbSchema}：当前 {eval.CurrentVersion} 不兼容",
        };
}
