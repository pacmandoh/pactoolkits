using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.Tests.ViewModels.Pages.Dashboard;

public sealed class DashboardTrendFilterTests
{
    [Fact]
    public async Task RefreshDrugCatalog_keeps_unlisted_drug_after_row_selection_apply()
    {
        const string drugId = "多潘立酮片";
        var lookup = new FakeLookup([]);
        var page = new DashboardViewModel(new NoopDashboardService(), lookup);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        using (page.BeginRowSelectionSuppress())
        {
            page.DrugText = drugId;
        }

        await page.TestRefreshDrugCatalogAsync(TestContext.Current.CancellationToken);

        Assert.Equal(drugId, page.DrugText);
    }

    [Fact]
    public async Task ReloadSpecs_keeps_trend_spec_when_catalog_specs_exclude_it()
    {
        const string drugId = "多潘立酮片";
        const string spec = "10mg*30片";
        var lookup = new FakeLookup([drugId], ["10mg*36片"]);
        var selectedSpec = new OptionItem(spec, spec);
        var page = new DashboardViewModel(new NoopDashboardService(), lookup)
        {
            DrugText = drugId,
            SelectedSpec = selectedSpec
        };
        page.SpecOptions.Clear();
        page.SpecOptions.Add(new OptionItem("", "全部规格"));
        page.SpecOptions.Add(selectedSpec);

        await page.TestReloadSpecsForDrugAsync(drugId);

        Assert.Equal(spec, page.SelectedSpec.Raw);
        Assert.Contains(page.SpecOptions, x => x.Raw == spec);
    }

    private sealed class FakeLookup(
        IReadOnlyList<string> drugIds,
        IReadOnlyList<string>? catalogSpecs = null) : ILookupCatalogService
    {
        private readonly IReadOnlyList<string> _catalogSpecs = catalogSpecs ?? [];

        public Task<IReadOnlyList<string>> GetClientIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult(drugIds);

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(
            string drugId,
            CancellationToken ct,
            bool forceRefresh = false)
        {
            return Task.FromResult(_catalogSpecs);
        }

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

    private sealed class NoopDashboardService : IDashboardService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
            => Task.FromResult(EmptySnapshot(request.RefreshDistributions));

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

        private static DashboardSnapshot EmptySnapshot(bool distributionsRefreshed)
        {
            var emptyTxns = new PagedResult<TraceTxnDto>([], 0);
            var emptyTrends = new PagedResult<TrendRowDto>([], 0);
            var emptyEntries = new PagedResult<TraceEntryLogDto>([], 0);
            var emptyAbnormal = new PagedResult<AbnormalRowDto>([], 0);
            return new DashboardSnapshot(
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
                DistributionsRefreshed: distributionsRefreshed,
                ChartClients: [],
                EntryChart: [],
                Abnormal: emptyAbnormal);
        }
    }
}
