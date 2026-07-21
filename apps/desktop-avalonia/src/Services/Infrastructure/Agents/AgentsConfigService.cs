using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>Agents Host/模块相关配置读写</summary>
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

    public async Task SetInjectorEnabledAsync(bool enabled, CancellationToken ct)
    {
        await _configStore.UpdateAsync(cfg =>
        {
            cfg.Agents.Injector.Enabled = enabled;
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
