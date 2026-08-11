using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class DrugIndexServiceSearchTests
{
    [Fact]
    public async Task SearchAsync_pinyin_query_reports_catalog_total_when_results_capped()
    {
        var catalog = Enumerable.Range(1, 12)
            .Select(i => new DrugIndexDto(
                DrugId: $"Drug{i:D3}",
                Spec: "1g",
                Qty: 1,
                RuleKey: null,
                PreTc: null,
                Note: null,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: null,
                Version: 1))
            .ToArray();

        var repo = new FakeDrugIndexRepo(
            search: (_, limit, _) => Task.FromResult<IReadOnlyList<DrugIndexDto>>(
                catalog.Take(limit).ToArray()),
            count: (_, _) => Task.FromResult(2));

        var service = new DrugIndexService(repo, new FixedCatalogCache(catalog));

        var result = await service.SearchAsync("d", limit: 10, CancellationToken.None);

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(12, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_chinese_query_uses_sql_total()
    {
        var repo = new FakeDrugIndexRepo(
            search: (_, limit, _) => Task.FromResult<IReadOnlyList<DrugIndexDto>>(
                Enumerable.Range(1, limit)
                    .Select(i => new DrugIndexDto(
                        DrugId: $"药{i}",
                        Spec: "1g",
                        Qty: 1,
                        RuleKey: null,
                        PreTc: null,
                        Note: null,
                        CreatedAt: DateTimeOffset.UtcNow,
                        UpdatedAt: null,
                        Version: 1))
                    .ToArray()),
            count: (_, _) => Task.FromResult(15));

        var service = new DrugIndexService(repo, new FixedCatalogCache([]));

        var result = await service.SearchAsync("药", limit: 10, CancellationToken.None);

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(15, result.TotalCount);
    }

    private sealed class FakeDrugIndexRepo(
        Func<string?, int, CancellationToken, Task<IReadOnlyList<DrugIndexDto>>> search,
        Func<string?, CancellationToken, Task<int>> count) : IDrugIndexRepo
    {
        public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
            => search(keyword, limit, ct);

        public Task<int> CountAsync(string? keyword, CancellationToken ct)
            => count(keyword, ct);

        public Task<IReadOnlyList<DrugIndexDto>> ListCatalogAsync(int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();

        public Task DeleteAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
            string sourceDrugId,
            string sourceSpec,
            string targetDrugId,
            string targetSpec,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
            DrugIndexDto source,
            DrugIndexDto target,
            string reason,
            string operatorName,
            string sourceTag,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FixedCatalogCache(IReadOnlyList<DrugIndexDto> rows) : IPinyinSearchCatalogCache
    {
        public Task<IReadOnlyList<DrugIndexDto>> GetCatalogRowsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult(rows);

        public Task<IReadOnlyList<string>> GetSearchTextsAsync(CancellationToken ct, bool forceRefresh = false)
            => throw new NotSupportedException();

        public void Invalidate()
        {
        }
    }
}
