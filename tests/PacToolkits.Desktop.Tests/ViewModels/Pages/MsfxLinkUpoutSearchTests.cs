using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests.ViewModels.Pages;

public sealed class MsfxLinkUpoutSearchTests
{
    [Fact]
    public async Task QueryUpstream_with_keyword_searches_whole_date_range_not_only_current_page()
    {
        var api = new FakeApi(serverTotal: 101);
        api.Pages[1] = Fill(100, i => $"BILL-{i:D3}");
        api.Pages[2] = [Upout("TARGET-002")];

        var page = CreatePage(api);
        page.UpstreamKeyword = "TARGET";

        await page.QueryUpstreamCommand.ExecuteAsync(null);

        Assert.Equal(2, api.ListCalls.Count);
        Assert.Collection(
            page.UpoutRows,
            row => Assert.Equal("TARGET-002", row.BillCode));
        Assert.Equal(1, page.UpoutTotal);
        Assert.False(page.HasUpoutNextPage);
    }

    [Fact]
    public async Task Paging_a_keyword_result_slices_locally_without_new_requests()
    {
        var api = new FakeApi(serverTotal: 100);
        api.Pages[1] = Fill(100, i => i <= 30 ? $"TARGET-{i:D3}" : $"BILL-{i:D3}");

        var page = CreatePage(api);
        page.UpstreamKeyword = "TARGET";

        await page.QueryUpstreamCommand.ExecuteAsync(null);

        Assert.Equal(30, page.UpoutTotal);
        Assert.Equal(2, page.UpoutTotalPages);
        Assert.Equal(20, page.UpoutRows.Count);
        Assert.True(page.HasUpoutNextPage);

        await page.NextUpoutPageCommand.ExecuteAsync(null);

        Assert.Equal(2, page.UpoutPage);
        Assert.Equal(10, page.UpoutRows.Count);
        Assert.Equal("TARGET-021", page.UpoutRows[0].BillCode);
        Assert.Single(api.ListCalls);
    }

    private static MsfxLink CreatePage(IMsfxApiClient api)
    {
        var page = new MsfxLink(
            api,
            null!,
            null!,
            null!,
            new FakeConfigStore(),
            new FakeUnlock(),
            NoopToast.Instance,
            null!,
            null!,
            new WorkspaceDirtyRefresh());
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());
        page.UpoutFromDate = new DateTime(2026, 8, 1);
        page.UpoutToDate = new DateTime(2026, 8, 31);
        return page;
    }

    private static MsfxApiCallResult OkCall()
        => new(true, 200, "ok", string.Empty, string.Empty, "REQ", "{}", string.Empty);

    private static IReadOnlyList<MsfxListUpoutItem> Fill(int count, Func<int, string> billCode)
        => Enumerable.Range(1, count).Select(i => Upout(billCode(i))).ToList();

    private static MsfxListUpoutItem Upout(string billCode)
        => new(
            BillCode: billCode,
            BillType: "TYPE",
            BillTime: "2026-08-01",
            BillUploadTime: "2026-08-01",
            PhysicName: "药品",
            PkgSpec: "10mg*10",
            PrepnSpec: "盒",
            PrepnCount: 1,
            CodeCount: 1,
            ProduceBatchNo: "BATCH",
            ExpireDate: "2027-08-01",
            FromEntName: "上游企业",
            ProduceEntName: "生产企业",
            FromRefUserId: "FROM",
            ToRefUserId: "TO",
            ConfirmStatus: string.Empty,
            DrugTag: string.Empty,
            IsCollectDrugBill: "0",
            IsSpecialDrugBill: "0",
            IsBloodProductBill: "0",
            IsBiologicalProductBill: "0",
            IsBotulinumBill: "0",
            VerifyStatus: string.Empty,
            RegulatedFlag: string.Empty,
            LogisticsStatus: "OK",
            Status: "OK");

    private sealed class FakeApi(long serverTotal) : IMsfxApiClient
    {
        public Dictionary<long, IReadOnlyList<MsfxListUpoutItem>> Pages { get; } = [];
        public List<MsfxListUpoutRequest> ListCalls { get; } = [];

        public Task<MsfxApiCallResult> ExecuteRawAsync(
            MsfxApiOptions options,
            string method,
            IReadOnlyDictionary<string, string?> bizParams,
            CancellationToken ct)
            => Task.FromResult(OkCall());

        public Task<MsfxListUpoutResult> GetYljgListUpoutAsync(
            MsfxApiOptions options,
            MsfxListUpoutRequest request,
            CancellationToken ct)
        {
            ListCalls.Add(request);
            var items = Pages.TryGetValue(request.Page, out var page) ? page : [];
            return Task.FromResult(new MsfxListUpoutResult(OkCall(), serverTotal, items));
        }

        public Task<MsfxListUpoutDetailResult> GetYljgListUpoutDetailAsync(
            MsfxApiOptions options,
            MsfxListUpoutDetailRequest request,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeConfigStore : IAppConfigStore
    {
        private readonly AppConfigRoot _root = new()
        {
            MsfxApi = new MsfxApiOptions
            {
                RefEntId = "REF",
                TimeoutSeconds = 20,
            },
        };

        public string ConfigPath => "/tmp/pactoolkits-test.config.json";

        public AppConfigRoot Load() => _root;

        public void Save(AppConfigRoot config) => throw new NotSupportedException();

        public Task SaveAsync(AppConfigRoot config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void Update(Action<AppConfigRoot> mutator) => mutator(_root);

        public Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default)
        {
            mutator(_root);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnlock : ISensitiveUnlockService
    {
        public event Action<string>? StateChanged
        {
            add { }
            remove { }
        }

        public UnlockScopeSnapshot GetSnapshot(string scopeKey)
            => new(false, DateTimeOffset.MinValue, 0, DateTimeOffset.MinValue);

        public void Refresh(string scopeKey)
        {
        }

        public void Lock(string scopeKey)
        {
        }

        public Task<bool> RequestUnlockAsync(SensitiveOpRequest request, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> RequireUnlockAsync(
            string scopeKey,
            string scene,
            string promptTitle,
            string promptHint,
            CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class NoopToast : IToastService
    {
        public static readonly NoopToast Instance = new();

        public void Success(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }

        public void Warn(string title, string message)
        {
        }

        public void Info(string title, string message)
        {
        }
    }
}
