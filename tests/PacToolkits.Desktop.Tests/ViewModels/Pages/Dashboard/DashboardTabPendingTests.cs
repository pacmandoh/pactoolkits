using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardTabPendingTests
{
    [Fact]
    public async Task Tab_pending_waits_for_unmounted_grid_overview_does_not()
    {
        var page = new Dashboard(new FakeDashboardService());
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        await page.TestRunReloadCoreAsync();

        Assert.False(page.IsTxnSectionPending);
        Assert.False(page.IsEntrySectionPending);
        Assert.True(page.IsTxnTabPending);
        Assert.True(page.IsEntryTabPending);
        Assert.True(page.IsAbnormalSectionPending);

        page.IsTxnDetailGridMounted = true;
        Assert.False(page.IsTxnTabPending);

        page.TxnPanelMode = page.TxnPanelModes[1];
        Assert.True(page.IsTxnTabPending);
        page.IsTxnTrendGridMounted = true;
        Assert.False(page.IsTxnTabPending);

        page.IsEntryGridMounted = true;
        Assert.False(page.IsEntryTabPending);
        page.IsAbnormalGridMounted = true;
        Assert.False(page.IsAbnormalSectionPending);
    }

    private sealed class FakeDashboardService : IDashboardService
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
        {
            var txn = new TraceTxnDto(
                1,
                TxnStatus.Commit,
                TxnBadge.Done,
                "t",
                "d",
                "s",
                1,
                DateTimeOffset.UnixEpoch,
                "pc-a");
            var txns = new PagedResult<TraceTxnDto>([txn], 1);
            var trends = new PagedResult<TrendRowDto>([new TrendRowDto(1, "n", "s", null, 0m, "1")], 1);
            var entries = new PagedResult<TraceEntryLogDto>(
                [
                    new TraceEntryLogDto(
                        DateTimeOffset.UnixEpoch,
                        "d",
                        "s",
                        1,
                        1,
                        1,
                        0,
                        "ok",
                        1,
                        "pc-a",
                        "src",
                        null),
                ],
                1);
            var abnormal = new PagedResult<AbnormalRowDto>(
                [new AbnormalRowDto("t", "d", "pc-a", TxnBadge.Warning, 1)],
                1);
            return Task.FromResult(
                new DashboardSnapshot(
                    ClientNames: ["pc-a"],
                    Kpi: new DashboardKpiDto(0, 0, 0, 0, 0, 0, 0, null, null),
                    Trend: [],
                    TxnsOverview: txns,
                    TxnsPage: txns,
                    TxnTrendPage: trends,
                    EntriesOverview: entries,
                    EntriesPage: entries,
                    TopClients: [("pc-a", 1L)],
                    ChartTrend: [],
                    ChartTxns: [],
                    DistributionsRefreshed: request.RefreshDistributions,
                    ChartClients: [],
                    EntryChart: [],
                    Abnormal: abnormal));
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
