using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.UiTests;

public sealed class DashboardTrendFilterTests
{
    [AvaloniaFact]
    public async Task Row_selection_reloads_specs_for_the_selected_drug()
    {
        const string drugId = "多潘立酮片";
        const string spec = "10mg*30片";
        var lookup = new FakeLookup(["10mg*36片"]);
        var page = new DashboardViewModel(new NoopDashboardService(), lookup);
        page.TestInjectServices(apiAvailability: new ReadyApi());
        page.SpecOptions.Add(new OptionItem("旧规格", "旧规格"));

        await page.OpenTrendDrugAsync(new TrendDrugItem { Name = drugId, Sub = spec });

        Assert.Equal(drugId, lookup.LastSpecsDrugId);
        Assert.DoesNotContain(page.SpecOptions, x => x.Raw == "旧规格");
        Assert.Contains(page.SpecOptions, x => x.Raw == "10mg*36片");
        Assert.Equal(spec, page.SelectedSpec.Raw);
    }

    private sealed class FakeLookup(IReadOnlyList<string> specs) : ILookupCatalogService
    {
        public string? LastSpecsDrugId { get; private set; }

        public Task<IReadOnlyList<string>> GetClientIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(
            string drugId,
            CancellationToken ct,
            bool forceRefresh = false)
        {
            LastSpecsDrugId = drugId;
            return Task.FromResult(specs);
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

    private sealed class ReadyApi : IApiAvailabilityService
    {
        public ApiAvailabilitySnapshot Current { get; } = new(
            ApiAvailabilityState.Ready,
            null,
            DateTimeOffset.UtcNow,
            true);

        public bool IsConfigured => true;
        public string? LastApiVersion => null;
        public string? LastContractVersion => null;
        public string? LastDatabase => null;
        public string? LastSchema => null;
        public string? LastSchemaVersion => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void Start() { }
        public Task ProbeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reset() { }
        public void Dispose() { }
    }
}
