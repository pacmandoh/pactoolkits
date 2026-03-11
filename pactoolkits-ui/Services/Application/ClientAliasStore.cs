using System;
using pactoolkits_ui.Services.Infrastructure;
using System.Collections.Generic;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.Services.Application;

public sealed class ClientAliasStore
{
    private static readonly object _lock = new();
    private readonly IAppConfigStore _configStore;
    private Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public ClientAliasStore(IAppConfigStore configStore)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_lock)
            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                var cfg = _configStore.Load();
                _aliases = cfg.ClientAliases is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(cfg.ClientAliases, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public void Save(IEnumerable<KeyValuePair<string, string>> items)
    {
        lock (_lock)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in items)
            {
                var k = (kv.Key).Trim();
                if (k.Length == 0) continue;

                var v = (kv.Value).Trim();
                if (v.Length == 0) continue;

                dict[k] = v;
            }

            _aliases = dict;

            try
            {
                var cfg = _configStore.Load();
                cfg.ClientAliases = new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
                _configStore.Save(cfg);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("ClientAliasStore", "alias.save.persist_fail", "Failed to persist alias map to config", ex);
            }
        }
    }

    public string? TryGet(string? machine)
    {
        if (string.IsNullOrWhiteSpace(machine)) return null;
        lock (_lock)
            return _aliases.TryGetValue(machine.Trim(), out var v) ? v : null;
    }
}
