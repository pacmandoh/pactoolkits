using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

/// <summary>向 Desktop 提供统一的 Agents 生命周期管理入口</summary>
public sealed class AgentsManager : IAgentsManager
{
    private readonly IReadOnlyDictionary<string, IAgentsRuntime> _runtimes;

    public AgentsManager(IEnumerable<IAgentsRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        var map = new Dictionary<string, IAgentsRuntime>(StringComparer.Ordinal);
        foreach (var runtime in runtimes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            var id = runtime.Descriptor.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("Agents runtime descriptor id is required.");
            }

            if (map.ContainsKey(id))
            {
                throw new InvalidOperationException($"Duplicate Agents runtime id: {id}");
            }

            map[id] = runtime;
        }

        _runtimes = new ReadOnlyDictionary<string, IAgentsRuntime>(map);
    }

    public IAgentsRuntime GetRequired(string id)
    {
        if (_runtimes.TryGetValue(id, out var runtime))
        {
            return runtime;
        }

        throw new KeyNotFoundException($"Unknown Agents id: {id}");
    }

    public Task<IReadOnlyDictionary<string, AgentsCommandResult>> SyncConfigAsync(
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, AgentsCommandResult>(StringComparer.Ordinal);
        foreach (var (id, runtime) in _runtimes)
        {
            ct.ThrowIfCancellationRequested();
            runtime.Reload();
            results[id] = new AgentsCommandResult(true, "配置已同步");
        }

        return Task.FromResult<IReadOnlyDictionary<string, AgentsCommandResult>>(
            new ReadOnlyDictionary<string, AgentsCommandResult>(results));
    }

    public async Task<IReadOnlyDictionary<string, AgentsCommandResult>> StopAllAsync(
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, AgentsCommandResult>(StringComparer.Ordinal);
        foreach (var (id, runtime) in _runtimes)
        {
            ct.ThrowIfCancellationRequested();
            results[id] = await runtime.StopAsync(ct).ConfigureAwait(false);
        }

        return new ReadOnlyDictionary<string, AgentsCommandResult>(results);
    }
}
