using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// 运行中模块入口的稳定热更观测
/// </summary>
internal sealed class HostModuleBinary
{
    private readonly Dictionary<string, (string Path, AgentsBinaryChange Change)> _watches =
        new(StringComparer.Ordinal);

    public void Sync(IReadOnlyList<(string Id, string EntryPath)> slots)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, entryPath) in slots)
        {
            keep.Add(id);
            if (string.IsNullOrWhiteSpace(entryPath))
            {
                _watches.Remove(id);
                continue;
            }

            var path = Path.GetFullPath(entryPath);
            if (_watches.TryGetValue(id, out var existing)
                && string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var change = new AgentsBinaryChange();
            change.Observe(AgentsBinaryStampIO.TryRead(path), active: false, out _);
            _watches[id] = (path, change);
        }

        foreach (var stale in _watches.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _watches.Remove(stale);
        }
    }

    /// <summary>
    /// 返回应重启的运行中 moduleId（入口稳定变更两次）
    /// </summary>
    public IReadOnlyList<string> DetectReloads(IReadOnlyList<(string Id, bool Alive)> states)
    {
        var reloads = new List<string>();
        foreach (var (id, alive) in states)
        {
            if (!_watches.TryGetValue(id, out var watch))
            {
                continue;
            }

            if (watch.Change.Observe(AgentsBinaryStampIO.TryRead(watch.Path), alive, out var changed))
            {
                reloads.Add(id);
                watch.Change.Accept(changed);
            }
        }

        return reloads;
    }
}
