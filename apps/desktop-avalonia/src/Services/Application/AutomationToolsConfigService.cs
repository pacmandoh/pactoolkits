using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

public sealed class AutomationConfigService : IAutomationConfigService
{
    private readonly IAppConfigStore _configStore;
    private readonly IAgentManager _agentManager;

    public AutomationConfigService(
        IAppConfigStore configStore,
        IAgentManager agentManager)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public AutomationConfigDto Load()
        => AutomationContractMapper.ToApplication(_configStore.Load().AutomationTools);

    public async Task SaveAsync(AutomationConfigDto options, CancellationToken ct)
    {
        var contract = AutomationContractMapper.ToContract(options);
        await _configStore.UpdateAsync(cfg =>
        {
            cfg.AutomationTools = contract;
            AppConfigStore.SyncInjectorFromTools(cfg, contract);
        }, ct).ConfigureAwait(false);
        var synchronization = await _agentManager.SynchronizeConfigurationAsync(ct).ConfigureAwait(false);
        foreach (var (agentId, result) in synchronization)
        {
            if (!result.Ok)
                throw new InvalidOperationException($"Agent 配置同步失败（{agentId}）：{result.Message}");
        }
    }

    public async Task SetAgentEnabledAsync(string agentId, bool enabled, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        await _configStore.UpdateAsync(cfg =>
        {
            cfg.Agents ??= new Dictionary<string, AgentInstanceConfig>(StringComparer.Ordinal);
            if (!cfg.Agents.TryGetValue(agentId, out var agent) || agent is null)
            {
                agent = new AgentInstanceConfig();
                cfg.Agents[agentId] = agent;
            }

            agent.Enabled = enabled;
        }, ct).ConfigureAwait(false);

        var synchronization = await _agentManager.SynchronizeConfigurationAsync(ct).ConfigureAwait(false);
        if (synchronization.TryGetValue(agentId, out var result) && !result.Ok)
            throw new InvalidOperationException($"Agent 配置同步失败（{agentId}）：{result.Message}");
    }
}
