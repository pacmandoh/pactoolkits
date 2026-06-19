using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IPinyinSearchCatalogCache
{
    Task<IReadOnlyList<DrugIndexDto>> GetCatalogRowsAsync(CancellationToken ct, bool forceRefresh = false);

    Task<IReadOnlyList<string>> GetSearchTextsAsync(CancellationToken ct, bool forceRefresh = false);

    void Invalidate();
}
