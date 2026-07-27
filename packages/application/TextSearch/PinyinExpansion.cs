using System.Collections.Concurrent;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.TextSearch;

/// <summary>
/// 将拼音首字母查询展开为匹配的药品 ID 和规格集合
/// </summary>
public static class PinyinExpansion
{
    public static async Task<KeywordSearchContext> ExpandDrugKeywordAsync(
        IPinyinSearchCatalogCache catalogCache,
        string? keyword,
        CancellationToken ct)
    {
        var kw = (keyword ?? string.Empty).Trim();
        if (kw.Length == 0 || !TextSearchHelper.LooksLikePinyinQuery(kw))
        {
            return KeywordSearchContext.Plain(kw);
        }

        var rows = await catalogCache.GetCatalogRowsAsync(ct).ConfigureAwait(false);
        return ExpandDrugKeyword(rows, kw);
    }

    public static KeywordSearchContext ExpandDrugKeyword(
        IReadOnlyList<DrugIndexDto> catalog,
        string? keyword)
    {
        var kw = (keyword ?? string.Empty).Trim();
        if (kw.Length == 0 || !TextSearchHelper.LooksLikePinyinQuery(kw))
        {
            return KeywordSearchContext.Plain(kw);
        }

        var drugIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in catalog)
        {
            if (PinyinInitialMatcher.IsMatch(kw, row.DrugId))
            {
                drugIds.Add(row.DrugId);
            }

            if (PinyinInitialMatcher.IsMatch(kw, row.Spec))
            {
                specs.Add(row.Spec);
            }
        }

        return new KeywordSearchContext(kw, drugIds.ToArray(), specs.ToArray());
    }
}

internal sealed class PinyinExpansionCache
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
