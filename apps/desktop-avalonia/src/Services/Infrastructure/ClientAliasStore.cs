using System;
using System.Collections.Generic;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public sealed class ClientAliasStore : IClientAliasStore
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
        {
            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyDictionary<string, string> Load()
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

            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyDictionary<string, string> Save(IEnumerable<KeyValuePair<string, string>> items)
    {
        lock (_lock)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in items)
            {
                var k = (kv.Key).Trim();
                if (k.Length == 0)
                {
                    continue;
                }

                var v = (kv.Value).Trim();
                if (v.Length == 0)
                {
                    continue;
                }

                dict[k] = v;
            }

            _aliases = dict;

            try
            {
                var aliases = new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
                _configStore.Update(cfg => cfg.ClientAliases = aliases);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("ClientAliasStore", "alias.save.persist_fail", "Failed to persist alias map to config", ex);
            }

            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
        }
    }

}
