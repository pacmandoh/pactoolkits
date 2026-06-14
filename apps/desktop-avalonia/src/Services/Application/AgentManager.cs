using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Agents;

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
                throw new InvalidOperationException("Agent runtime descriptor id is required.");

            if (map.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate agent runtime id: {id}");

            map[id] = runtime;
        }

        _runtimes = new ReadOnlyDictionary<string, IAgentRuntime>(map);
    }

    public IReadOnlyCollection<AgentDescriptor> Descriptors
        => _runtimes.Values.Select(runtime => runtime.Descriptor).ToArray();

    public IAgentRuntime GetRequired(string agentId)
    {
        if (_runtimes.TryGetValue(agentId, out var runtime))
            return runtime;

        throw new KeyNotFoundException($"Unknown agent id: {agentId}");
    }

    public bool TryGet(string agentId, out IAgentRuntime? runtime)
    {
        var found = _runtimes.TryGetValue(agentId, out var value);
        runtime = value;
        return found;
    }
}
