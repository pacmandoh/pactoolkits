using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardFilterInitTests
{
    [Fact]
    public async Task First_reload_loads_filter_catalog_when_api_ready()
    {
        var dashboard = new FakeDashboardService();
        var lookup = new FakeLookup(["drug-a", "drug-b"]);
        var page = new Dashboard(dashboard, lookup);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        await page.TestRunReloadCoreAsync();

        Assert.Equal(1, lookup.DrugIdsCalls);
        Assert.Contains(page.DrugOptions, x => x.Raw == "drug-a");
        Assert.Contains(page.DrugOptions, x => x.Raw == "drug-b");
        Assert.Equal(1, dashboard.SnapshotCalls);
    }

    private sealed class FakeLookup(IReadOnlyList<string> drugIds) : ILookupCatalogService
    {
        public int DrugIdsCalls { get; private set; }

        public Task<IReadOnlyList<string>> GetClientIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
        {
            DrugIdsCalls++;
            return Task.FromResult(drugIds);
        }

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(
            string drugId,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> ResolveCanonicalDrugIdAsync(
            string? input,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<string?>(null);

        public Task<int?> GetQtyAsync(
            string? drugId,
            string? spec,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult<int?>(null);

        public Task<bool> IsDeprecatedDrugIdAsync(
            string? drugId,
            CancellationToken ct,
            bool forceRefresh = false)
            => Task.FromResult(false);

        public void InvalidateDrugCatalog()
        {
        }
    }

    private sealed class FakeDashboardService : IDashboardService
    {
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
    }
}
