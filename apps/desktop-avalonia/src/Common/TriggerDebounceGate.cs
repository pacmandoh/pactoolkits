using System;
using System.Collections.Generic;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// Lightweight per-key trigger debounce gate for UI actions.
/// </summary>
public sealed class TriggerDebounceGate
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _lastByKey = new(StringComparer.Ordinal);

    public bool Skip(string key, TimeSpan interval)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            if (_lastByKey.TryGetValue(key, out var last) && now - last < interval)
            {
                return true;
            }

            _lastByKey[key] = now;
            return false;
        }
    }
}
