using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class LookupCatalogServiceTests
{
    [Fact]
    public async Task GetDrugIdsAsync_returns_empty_without_hitting_repo_when_access_blocked()
    {
        var guard = new DatabaseAccessGuard();
        guard.Block("数据库版本不兼容");
        var repo = new TrackingDashboardRepo();
        var service = new LookupCatalogService(repo, new EmptyDrugIndexRepo(), guard);

        var first = await service.GetDrugIdsAsync(CancellationToken.None);
        var second = await service.GetDrugIdsAsync(CancellationToken.None);

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(0, repo.GetDrugIdsCallCount);
    }

    [Fact]
    public async Task GetDrugIdsAsync_uses_repo_when_access_unblocked()
    {
        var guard = new DatabaseAccessGuard();
        var repo = new TrackingDashboardRepo();
        var service = new LookupCatalogService(repo, new EmptyDrugIndexRepo(), guard);

        var drugs = await service.GetDrugIdsAsync(CancellationToken.None);

        Assert.Equal(["DrugA"], drugs);
        Assert.Equal(1, repo.GetDrugIdsCallCount);
    }

    [Fact]
    public async Task GetDrugIdsAsync_does_not_return_cached_values_after_access_becomes_blocked()
    {
        var guard = new DatabaseAccessGuard();
        var repo = new TrackingDashboardRepo();
        var service = new LookupCatalogService(repo, new EmptyDrugIndexRepo(), guard);

        var initial = await service.GetDrugIdsAsync(CancellationToken.None);
        Assert.Equal(["DrugA"], initial);

        guard.Block("blocked");

        var blocked = await service.GetDrugIdsAsync(CancellationToken.None);

        Assert.Empty(blocked);
        Assert.Equal(1, repo.GetDrugIdsCallCount);
    }

    private sealed class TrackingDashboardRepo : IDashboardRepo
    {
        public int GetDrugIdsCallCount { get; private set; }

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct)
        {
            GetDrugIdsCallCount++;
            return Task.FromResult<IReadOnlyList<string>>(["DrugA"]);
        }

        public Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<(string Client, long Value)>> GetClientsAsync(DashboardQuery q, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<DashboardKpiDto> GetKpisAsync(DashboardQuery q, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<TrendRowDto>> GetTrendPageAsync(DashboardQuery q, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<TraceTxnDto>> GetRecentTxnsPageAsync(DashboardQuery q, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<AbnormalRowDto>> GetAbnormalQueuePageAsync(DashboardQuery q, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<PagedResult<TraceEntryLogDto>> GetEntryLogsPageAsync(DashboardQuery q, int page, int pageSize, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class EmptyDrugIndexRepo : IDrugIndexRepo
    {
        public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
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
}
