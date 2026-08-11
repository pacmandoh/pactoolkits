using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 药品索引表 CRUD/检索与主键修复数据访问
/// </summary>
public interface IDrugIndexRepo
{
    Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct);

    Task<int> CountAsync(string? keyword, CancellationToken ct);

    Task<IReadOnlyList<DrugIndexDto>> ListCatalogAsync(int limit, CancellationToken ct);

    Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct);

    Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct);

    Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct);

    Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct);

    Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct);

    Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
        DrugIndexDto source,
        DrugIndexDto target,
        string reason,
        string operatorName,
        string sourceTag,
        CancellationToken ct);
}
