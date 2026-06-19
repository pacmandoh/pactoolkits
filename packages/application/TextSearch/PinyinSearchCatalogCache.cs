using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.TextSearch;

public sealed class PinyinSearchCatalogCache : IPinyinSearchCatalogCache
{
    private const int CatalogLimit = 10_000;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly IDrugIndexRepo _drugIndexRepo;
    private readonly IDatabaseAccessGuard _accessGuard;
    private readonly object _gate = new();
    private CacheEntry? _cache;

    public PinyinSearchCatalogCache(IDrugIndexRepo drugIndexRepo, IDatabaseAccessGuard accessGuard)
    {
        _drugIndexRepo = drugIndexRepo ?? throw new ArgumentNullException(nameof(drugIndexRepo));
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
    }

    public async Task<IReadOnlyList<string>> GetSearchTextsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        var rows = await GetCatalogRowsAsync(ct, forceRefresh).ConfigureAwait(false);
        var texts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.DrugId))
            {
                texts.Add(row.DrugId);
            }

            if (!string.IsNullOrWhiteSpace(row.Spec))
            {
                texts.Add(row.Spec);
            }
        }

        return texts.ToArray();
    }

    public async Task<IReadOnlyList<DrugIndexDto>> GetCatalogRowsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (_accessGuard.IsBlocked)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (_cache is not null && _cache.ExpiresAt > now)
                {
                    return _cache.Rows;
                }
            }
        }

        var rows = await _drugIndexRepo.ListCatalogAsync(CatalogLimit, ct).ConfigureAwait(false);
        lock (_gate)
        {
            _cache = new CacheEntry(rows, now.Add(Ttl));
        }

        return rows;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
        }
    }

    private sealed record CacheEntry(IReadOnlyList<DrugIndexDto> Rows, DateTimeOffset ExpiresAt);
}
