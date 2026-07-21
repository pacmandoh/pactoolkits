namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 药品/规格查找目录缓存（短 TTL，供自动完成与校验）
/// </summary>
public interface ILookupCatalogService
{
    Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false);

    Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct, bool forceRefresh = false);

    Task<string?> ResolveCanonicalDrugIdAsync(string? input, CancellationToken ct, bool forceRefresh = false);

    Task<int?> GetQtyAsync(string? drugId, string? spec, CancellationToken ct, bool forceRefresh = false);

    Task<bool> IsDeprecatedDrugIdAsync(string? drugId, CancellationToken ct, bool forceRefresh = false);

    void InvalidateDrugCatalog();
}
