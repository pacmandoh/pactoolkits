using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardFilterInitTests
{
    [Fact]
    public async Task Initialize_loads_filter_catalog_when_local_db_access_blocked()
    {
        var dashboard = new FakeDashboardService(["drug-a", "drug-b"]);
        var page = new Dashboard(dashboard);
        var guard = new FakeAccessGuard();
        guard.Block("schema incompatible");
        page.TestInjectDbServices(accessGuard: guard);

        // 本机 DB 门禁不得挡住筛选目录加载
        Assert.True(guard.IsBlocked);

        await page.TestInitializeAsync();

        Assert.Equal(1, dashboard.DrugIdsCalls);
        Assert.Contains(page.DrugOptions, x => x.Raw == "drug-a");
        Assert.Contains(page.DrugOptions, x => x.Raw == "drug-b");
        Assert.Equal(1, dashboard.SnapshotCalls);
    }

    private sealed class FakeAccessGuard : IDbAccessGuard
    {
        public bool IsBlocked { get; private set; }

        public string? BlockReason { get; private set; }

        public void Block(string reason)
        {
            IsBlocked = true;
            BlockReason = reason;
        }

        public void Clear()
        {
            IsBlocked = false;
            BlockReason = null;
        }

        public void ThrowIfBlocked()
        {
            if (IsBlocked)
            {
                throw new InvalidOperationException(BlockReason ?? "blocked");
            }
        }
    }

    private sealed class FakeDashboardService(IReadOnlyList<string> drugIds) : IDashboardService
    {
        public int DrugIdsCalls { get; private set; }

        public int SnapshotCalls { get; private set; }

        public Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
        {
            SnapshotCalls++;
            var emptyTxns = new PagedResult<TraceTxnDto>([], 0);
            var emptyTrends = new PagedResult<TrendRowDto>([], 0);
            var emptyEntries = new PagedResult<TraceEntryLogDto>([], 0);
            var emptyAbnormal = new PagedResult<AbnormalRowDto>([], 0);
            return Task.FromResult(
                new DashboardSnapshot(
                    ClientNames: [],
                    Kpi: new DashboardKpiDto(0, 0, 0, 0, 0, 0, 0, null, null),
                    Trend: [],
                    TxnsOverview: emptyTxns,
                    TxnsPage: emptyTxns,
                    TxnTrendPage: emptyTrends,
                    EntriesOverview: emptyEntries,
                    EntriesPage: emptyEntries,
                    TopClients: [],
                    ChartTrend: [],
                    ChartTxns: [],
                    DistributionsRefreshed: request.RefreshDistributions,
                    ChartClients: [],
                    EntryChart: [],
                    Abnormal: emptyAbnormal));
        }

        public Task<PagedResult<TraceTxnDto>> GetTxnPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<TraceTxnDto>([], 0));

        public Task<PagedResult<TrendRowDto>> GetTxnTrendPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<TrendRowDto>([], 0));

        public Task<PagedResult<TraceEntryLogDto>> GetEntryPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<TraceEntryLogDto>([], 0));

        public Task<PagedResult<AbnormalRowDto>> GetAbnormalPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<AbnormalRowDto>([], 0));

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct)
        {
            DrugIdsCalls++;
            return Task.FromResult(drugIds);
        }

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
