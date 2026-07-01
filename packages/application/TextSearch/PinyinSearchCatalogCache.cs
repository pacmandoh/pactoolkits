using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.TextSearch;

public sealed class PinyinSearchCatalogCache : IPinyinSearchCatalogCache
{
    private const int CatalogLimit = 10_000;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly IDrugIndexRepo _drugIndexRepo;
    private readonly IDbAccessGuard _accessGuard;
    private readonly object _gate = new();
    private CacheEntry? _cache;
    private int _generation;

    public PinyinSearchCatalogCache(IDrugIndexRepo drugIndexRepo, IDbAccessGuard accessGuard)
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
        var generationAtStart = 0;
        lock (_gate)
        {
            generationAtStart = _generation;
            if (!forceRefresh && _cache is not null && _cache.ExpiresAt > now)
            {
                return _cache.Rows;
            }
        }

        var rows = await _drugIndexRepo.ListCatalogAsync(CatalogLimit, ct).ConfigureAwait(false);
        lock (_gate)
        {
            if (generationAtStart == _generation)
            {
                _cache = new CacheEntry(rows, now.Add(Ttl));
            }
        }

        return rows;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
            unchecked
            {
                _generation++;
            }
        }
    }

    private sealed record CacheEntry(IReadOnlyList<DrugIndexDto> Rows, DateTimeOffset ExpiresAt);
}
