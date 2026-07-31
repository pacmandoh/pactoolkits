using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Desktop.Tests;

public sealed class InventoryStockRowEditOccTests
{
    [Fact]
    public async Task ApplyStockRowEdits_aggregates_success()
    {
        var repo = new FakeInventoryRepo { NextVersion = 2 };
        var service = CreateService(repo);

        var result = await service.ApplyStockRowEditsAsync(
            [
                new StockRowEditRequest("T1", ExpectedVersion: 1, NewTraceCode: "T2", NewRemain: 3),
            ],
            new TraceCodeValidationRule(RequiredLength: 2, Pattern: string.Empty),
            CancellationToken.None);

        Assert.Equal(1, result.SavedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Conflicts);
        Assert.Single(result.Saved);
        Assert.Equal(2, result.Saved[0].NewVersion);
        Assert.Equal(("T1", 1L, "T2", 3), repo.LastUpdate);
    }

    [Fact]
    public async Task ApplyStockRowEdits_counts_concurrency_conflict_separately()
    {
        var repo = new FakeInventoryRepo { ThrowConcurrency = true };
        var service = CreateService(repo);

        var result = await service.ApplyStockRowEditsAsync(
            [
                new StockRowEditRequest("T1", ExpectedVersion: 0, NewTraceCode: null, NewRemain: 1),
            ],
            new TraceCodeValidationRule(RequiredLength: 2, Pattern: string.Empty),
            CancellationToken.None);

        Assert.Equal(0, result.SavedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains("其他终端", result.LastError);
        Assert.Single(result.Conflicts);
        Assert.Equal("T1", result.Conflicts[0].MatchTraceCode);
    }

    [Fact]
    public async Task ApplyStockRowEdits_rejects_invalid_trace_format()
    {
        var repo = new FakeInventoryRepo();
        var service = CreateService(repo);

        var result = await service.ApplyStockRowEditsAsync(
            [
                new StockRowEditRequest("T1", ExpectedVersion: 0, NewTraceCode: "bad", NewRemain: null),
            ],
            new TraceCodeValidationRule(RequiredLength: 20, Pattern: string.Empty),
            CancellationToken.None);

        Assert.Equal(0, result.SavedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Empty(result.Conflicts);
        Assert.Null(repo.LastUpdate);
    }

    private static InventoryOverviewService CreateService(IInventoryOverviewRepo repo)
        => new(repo, new UnusedDrugIndexRepo(), new UnusedPinyinCache());

    private sealed class FakeInventoryRepo : IInventoryOverviewRepo
    {
        public bool ThrowConcurrency { get; set; }

        public long NextVersion { get; set; } = 1;

        public (string Match, long Version, string? Trace, int? Remain)? LastUpdate { get; private set; }

        public Task<long> UpdateStockRowAsync(
            string matchTraceCode,
            long expectedVersion,
            string? newTraceCode,
            int? newRemain,
            CancellationToken ct)
        {
            if (ThrowConcurrency)
            {
                throw new TracePoolConcurrencyException(
                    "该记录已被其他终端修改，请刷新后重试",
                    current: null);
            }

            LastUpdate = (matchTraceCode, expectedVersion, newTraceCode, newRemain);
            return Task.FromResult(NextVersion);
        }

        public Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
            KeywordSearchContext keyword, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
            KeywordSearchContext keyword, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
            KeywordSearchContext keyword, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
            KeywordSearchContext keyword, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> DeleteStockByTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<StockReassignApplyResultDto> ReassignStockByTraceCodeAsync(
            string traceCode, string targetDrugId, string targetSpec, int targetQty,
            string reason, string operatorName, string source, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<StockReassignPreviewDto> PreviewStockReassignByKeywordAsync(
            KeywordSearchContext keyword, string targetDrugId, string targetSpec, int targetQty,
            int sampleLimit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<StockReassignApplyResultDto> ReassignStockByKeywordAsync(
            KeywordSearchContext keyword, string targetDrugId, string targetSpec, int targetQty,
            string reason, string operatorName, string source, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class UnusedDrugIndexRepo : IDrugIndexRepo
    {
        public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<int> CountAsync(string? keyword, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DrugIndexDto>> ListCatalogAsync(int limit, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
            => Task.FromResult<DrugIndexDto?>(null);

        public Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct)
            => throw new NotSupportedException();

        public Task DeleteAsync(string drugId, string spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
            string sourceDrugId, string sourceSpec, string targetDrugId, string targetSpec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
            DrugIndexDto source, DrugIndexDto target, string reason, string operatorName, string sourceTag,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class UnusedPinyinCache : IPinyinSearchCatalogCache
    {
        public Task<IReadOnlyList<DrugIndexDto>> GetCatalogRowsAsync(CancellationToken ct, bool forceRefresh = false)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetSearchTextsAsync(CancellationToken ct, bool forceRefresh = false)
            => throw new NotSupportedException();

        public void Invalidate()
        {
        }
    }
}
