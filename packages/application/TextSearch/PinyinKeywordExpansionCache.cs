using System.Collections.Concurrent;

namespace PacToolkits.Application.TextSearch;

internal sealed class PinyinKeywordExpansionCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);
    private const int MaxEntries = 256;

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string keyword, out string[][]? exactPerToken)
    {
        exactPerToken = null;
        if (!_entries.TryGetValue(keyword, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (entry is not null)
            {
                _entries.TryRemove(keyword, out _);
            }

            return false;
        }

        exactPerToken = entry.ExactPerToken;
        return true;
    }

    public void Set(string keyword, string[][]? exactPerToken)
    {
        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }

        _entries[keyword] = new CacheEntry(exactPerToken, DateTimeOffset.UtcNow.Add(Ttl));
    }

    private sealed record CacheEntry(string[][]? ExactPerToken, DateTimeOffset ExpiresAt);
}
