using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>持久化 Agents 全局配置与模块启用状态，并将变更同步到运行时</summary>
public sealed class AgentsConfigService : IAgentsConfigService
{
    private readonly IAppConfigStore _configStore;
    private readonly IAgentsManager _agentsManager;

    public AgentsConfigService(
        IAppConfigStore configStore,
        IAgentsManager agentsManager)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _agentsManager = agentsManager ?? throw new ArgumentNullException(nameof(agentsManager));
    }

    public AgentsConfigDto Load()
        => AgentsContractMapper.ToApplication(_configStore.Load().Agents);

    public async Task SaveAsync(AgentsConfigDto options, CancellationToken ct)
    {
        var contract = AgentsContractMapper.ToContract(options);
        await _configStore.UpdateAsync(cfg =>
        {
            cfg.Agents = contract;
        }, ct).ConfigureAwait(false);
        await SyncOrThrowAsync(ct).ConfigureAwait(false);
    }

    public async Task SetModuleEnabledAsync(string moduleId, bool enabled, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("moduleId is required", nameof(moduleId));
        }

        await _configStore.UpdateAsync(cfg =>
        {
            cfg.Agents.Modules ??= new Dictionary<string, ModuleOptions>(
                StringComparer.Ordinal);
            if (!cfg.Agents.Modules.TryGetValue(moduleId, out var module))
            {
                module = new ModuleOptions();
                cfg.Agents.Modules[moduleId] = module;
            }

            module.Enabled = enabled;
        }, ct).ConfigureAwait(false);

        await SyncOrThrowAsync(ct).ConfigureAwait(false);
    }

    private async Task SyncOrThrowAsync(CancellationToken ct)
    {
        var synchronization = await _agentsManager.SyncConfigAsync(ct).ConfigureAwait(false);
        foreach (var (id, result) in synchronization)
        {
            if (!result.Ok)
            {
                throw new InvalidOperationException($"Agents 配置同步失败（{id}）：{result.Message}");
            }
        }
    }
}
