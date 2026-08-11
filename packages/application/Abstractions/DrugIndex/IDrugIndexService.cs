using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 定义药品索引检索、保存规则和主键修复的业务用例契约
/// </summary>
public interface IDrugIndexService
{
    Task<DrugIndexSearchResult> SearchAsync(string? keyword, int limit, CancellationToken ct);

    Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugIndexSaveResult> SaveAsync(DrugIndexSaveRequest request, CancellationToken ct);

    Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct);

    Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct);

    Task<DrugKeyFixCommitResult> ApplyKeyFixAsync(DrugKeyFixRequest request, CancellationToken ct);
}
