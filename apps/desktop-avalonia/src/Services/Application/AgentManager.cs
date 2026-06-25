using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Commands;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed class AgentManager : IAgentManager
{
    private readonly IReadOnlyDictionary<string, IAgentRuntime> _runtimes;

    public AgentManager(IEnumerable<IAgentRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        var map = new Dictionary<string, IAgentRuntime>(StringComparer.Ordinal);
        foreach (var runtime in runtimes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            var id = runtime.Descriptor.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("Agent runtime descriptor id is required.");
            }

            if (map.ContainsKey(id))
            {
                throw new InvalidOperationException($"Duplicate agent runtime id: {id}");
            }

            map[id] = runtime;
        }

        _runtimes = new ReadOnlyDictionary<string, IAgentRuntime>(map);
    }

    public IAgentRuntime GetRequired(string agentId)
    {
        if (_runtimes.TryGetValue(agentId, out var runtime))
        {
            return runtime;
        }

        throw new KeyNotFoundException($"Unknown agent id: {agentId}");
    }

    public async Task<IReadOnlyDictionary<string, ToolCommandResult>> SyncConfigAsync(
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, ToolCommandResult>(StringComparer.Ordinal);
        foreach (var (agentId, runtime) in _runtimes)
        {
            ct.ThrowIfCancellationRequested();
            runtime.Reload();
            results[agentId] = !runtime.IsEnabled && runtime.IsRunning
                ? await runtime.StopAsync(ct).ConfigureAwait(false)
                : new ToolCommandResult(true, runtime.IsEnabled ? "配置已同步" : "Agent 已禁用");
        }

        return new ReadOnlyDictionary<string, ToolCommandResult>(results);
    }

    public async Task<IReadOnlyDictionary<string, ToolCommandResult>> StopAllAsync(
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, ToolCommandResult>(StringComparer.Ordinal);
        foreach (var (agentId, runtime) in _runtimes)
        {
            ct.ThrowIfCancellationRequested();
            results[agentId] = await runtime.StopAsync(ct).ConfigureAwait(false);
        }

        return new ReadOnlyDictionary<string, ToolCommandResult>(results);
    }
}
