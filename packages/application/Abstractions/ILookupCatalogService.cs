namespace PacToolkits.Application.Abstractions;

public interface ILookupCatalogService
{
    Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false);

    Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct, bool forceRefresh = false);

    Task<string?> ResolveCanonicalDrugIdAsync(string? input, CancellationToken ct, bool forceRefresh = false);

    Task<int?> GetQtyAsync(string? drugId, string? spec, CancellationToken ct, bool forceRefresh = false);

    Task<bool> IsDeprecatedDrugIdAsync(string? drugId, CancellationToken ct, bool forceRefresh = false);

    void InvalidateDrugCatalog();
}
