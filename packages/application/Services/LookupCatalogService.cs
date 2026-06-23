using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

public sealed class LookupCatalogService : ILookupCatalogService
{
    private static readonly TimeSpan DrugIdsTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SpecsTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CanonicalTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan QtyTtl = TimeSpan.FromSeconds(10);

    private readonly IDashboardRepo _dashboardRepo;
    private readonly IDrugIndexRepo _drugIndexRepo;
    private readonly IDbAccessGuard _accessGuard;
    private readonly object _gate = new();

    private CacheItem<IReadOnlyList<string>>? _drugIdsCache;
    private readonly Dictionary<string, CacheItem<IReadOnlyList<string>>> _specsByDrug = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CacheItem<string?>> _canonicalDrugByInput = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CacheItem<int?>> _qtyByDrugSpec = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CacheItem<bool>> _deprecatedDrugByInput = new(StringComparer.OrdinalIgnoreCase);

    public LookupCatalogService(
        IDashboardRepo dashboardRepo,
        IDrugIndexRepo drugIndexRepo,
        IDbAccessGuard accessGuard)
    {
        _dashboardRepo = dashboardRepo ?? throw new ArgumentNullException(nameof(dashboardRepo));
        _drugIndexRepo = drugIndexRepo ?? throw new ArgumentNullException(nameof(drugIndexRepo));
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
    }

    public async Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (IsCatalogAccessBlocked())
        {
            return Array.Empty<string>();
        }

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (TryGetValid(_drugIdsCache, now, out var cached))
                {
                    return cached;
                }
            }
        }

        var rows = await _dashboardRepo.GetDrugIdsAsync(ct).ConfigureAwait(false);
        var normalized = rows.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        lock (_gate)
        {
            _drugIdsCache = new CacheItem<IReadOnlyList<string>>(normalized, now.Add(DrugIdsTtl));
        }

        return normalized;
    }

    public async Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct, bool forceRefresh = false)
    {
        if (IsCatalogAccessBlocked())
        {
            return Array.Empty<string>();
        }

        var key = InputNormalizer.Normalize(drugId);
        if (string.IsNullOrWhiteSpace(key))
        {
            return Array.Empty<string>();
        }

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (_specsByDrug.TryGetValue(key, out var cache) &&
                    TryGetValid(cache, now, out var cached))
                {
                    return cached;
                }
            }
        }

        var rows = await _dashboardRepo.GetSpecsByDrugAsync(key, ct).ConfigureAwait(false);
        var normalized = rows.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        lock (_gate)
        {
            _specsByDrug[key] = new CacheItem<IReadOnlyList<string>>(normalized, now.Add(SpecsTtl));
        }

        return normalized;
    }

    public async Task<string?> ResolveCanonicalDrugIdAsync(string? input, CancellationToken ct, bool forceRefresh = false)
    {
        if (IsCatalogAccessBlocked())
        {
            return null;
        }

        var key = InputNormalizer.Normalize(input);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (_canonicalDrugByInput.TryGetValue(key, out var cache) &&
                    TryGetValid(cache, now, out var cached))
                {
                    return cached;
                }
            }
        }

        IReadOnlyList<string> allDrugIds;
        lock (_gate)
        {
            if (!forceRefresh && TryGetValid(_drugIdsCache, now, out var drugIdsCached))
            {
                allDrugIds = drugIdsCached;
            }
            else
            {
                allDrugIds = Array.Empty<string>();
            }
        }

        if (allDrugIds.Count == 0)
        {
            allDrugIds = await GetDrugIdsAsync(ct, forceRefresh: forceRefresh).ConfigureAwait(false);
        }

        var canonical = allDrugIds.FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
        lock (_gate)
        {
            _canonicalDrugByInput[key] = new CacheItem<string?>(canonical, now.Add(CanonicalTtl));
        }

        return canonical;
    }

    public async Task<int?> GetQtyAsync(string? drugId, string? spec, CancellationToken ct, bool forceRefresh = false)
    {
        if (IsCatalogAccessBlocked())
        {
            return null;
        }

        var drug = InputNormalizer.Normalize(drugId);
        var specValue = InputNormalizer.Normalize(spec);
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(specValue))
        {
            return null;
        }

        var key = $"{drug}|{specValue}";
        var now = DateTimeOffset.UtcNow;

        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (_qtyByDrugSpec.TryGetValue(key, out var cache) &&
                    TryGetValid(cache, now, out var cached))
                {
                    return cached;
                }
            }
        }

        var dto = await _drugIndexRepo.GetByKeyAsync(drug, specValue, ct).ConfigureAwait(false);
        int? qty = dto is null || IsDeprecatedNote(dto.Note) ? null : dto.Qty;
        lock (_gate)
        {
            _qtyByDrugSpec[key] = new CacheItem<int?>(qty, now.Add(QtyTtl));
        }

        return qty;
    }

    public async Task<bool> IsDeprecatedDrugIdAsync(string? drugId, CancellationToken ct, bool forceRefresh = false)
    {
        if (IsCatalogAccessBlocked())
        {
            return false;
        }

        var key = InputNormalizer.Normalize(drugId);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh)
        {
            lock (_gate)
            {
                if (_deprecatedDrugByInput.TryGetValue(key, out var cache) &&
                    TryGetValid(cache, now, out var cached))
                {
                    return cached;
                }
            }
        }

        var isDeprecated = await _drugIndexRepo.IsDrugDeprecatedAsync(key, ct).ConfigureAwait(false);

        lock (_gate)
        {
            _deprecatedDrugByInput[key] = new CacheItem<bool>(isDeprecated, now.Add(CanonicalTtl));
        }

        return isDeprecated;
    }

    public void InvalidateDrugCatalog()
    {
        lock (_gate)
        {
            _drugIdsCache = null;
            _specsByDrug.Clear();
            _canonicalDrugByInput.Clear();
            _qtyByDrugSpec.Clear();
            _deprecatedDrugByInput.Clear();
        }
    }

    private bool IsCatalogAccessBlocked()
    {
        if (!_accessGuard.IsBlocked)
        {
            return false;
        }

        InvalidateDrugCatalog();
        return true;
    }

    private static bool TryGetValid<T>(CacheItem<T>? cache, DateTimeOffset now, out T value)
    {
        if (cache is not null && cache.ExpiresAtUtc > now)
        {
            value = cache.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private sealed record CacheItem<T>(T Value, DateTimeOffset ExpiresAtUtc);

    private static bool IsDeprecatedNote(string? note)
        => !string.IsNullOrWhiteSpace(note)
           && note.Contains("弃用", StringComparison.Ordinal);
}
