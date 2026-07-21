using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 拼音检索用药品目录缓存
/// </summary>
public interface IPinyinSearchCatalogCache
{
    Task<IReadOnlyList<DrugIndexDto>> GetCatalogRowsAsync(CancellationToken ct, bool forceRefresh = false);

    Task<IReadOnlyList<string>> GetSearchTextsAsync(CancellationToken ct, bool forceRefresh = false);

    void Invalidate();
}
