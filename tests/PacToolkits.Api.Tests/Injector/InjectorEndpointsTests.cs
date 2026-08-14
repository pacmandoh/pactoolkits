using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class InjectorEndpointsTests
{
    [Fact]
    public async Task Reserve_rejects_anonymous()
    {
        await using var factory = new ApiFactory { Injector = new FakeInjector() };
        using var client = factory.CreateClient();

        using var request = PostJson("/v1/injector/txn/reserve", """
            {"txnId":"t1","clientId":"c1","drugId":"d1","spec":"s1","wholeN":0,"remNeed":1}
            """);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reserve_returns_items()
    {
        var injector = new FakeInjector();
        await using var factory = new ApiFactory { Injector = injector };
        using var client = await AuthorizeAsync(factory);

        using var request = PostJson("/v1/injector/txn/reserve", """
            {"txnId":"t1","clientId":"c1","drugId":"d1","spec":"s1","wholeN":0,"remNeed":1}
            """);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("t1", injector.LastTxnId);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("sumTake").GetInt32());
        Assert.Equal("CODE1", doc.RootElement.GetProperty("items")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Commit_and_rollback_pass_txn_id()
    {
        var injector = new FakeInjector();
        await using var factory = new ApiFactory { Injector = injector };
        using var client = await AuthorizeAsync(factory);

        using var commitReq = PostJson("/v1/injector/txn/commit", """{"txnId":"t-commit"}""");
        using var commit = await client.SendAsync(commitReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        Assert.Equal("t-commit", injector.LastCommitTxnId);

        using var rollbackReq = PostJson("/v1/injector/txn/rollback", """{"txnId":"t-roll"}""");
        using var rollback = await client.SendAsync(rollbackReq, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("t-roll", injector.LastRollbackTxnId);
    }

    [Fact]
    public async Task Cleanup_returns_cleaned()
    {
        var injector = new FakeInjector { CleanupCount = 2 };
        await using var factory = new ApiFactory { Injector = injector };
        using var client = await AuthorizeAsync(factory);

        using var request = PostJson("/v1/injector/txn/cleanup", """{"timeoutMinutes":10,"maxBatch":50}""");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, injector.LastCleanupMinutes);
        Assert.Equal(50, injector.LastCleanupBatch);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(2, doc.RootElement.GetProperty("cleaned").GetInt32());
    }

    [Fact]
    public async Task Warehouse_success_requires_query()
    {
        await using var factory = new ApiFactory { Injector = new FakeInjector() };
        using var client = await AuthorizeAsync(factory);

        using var response = await client.GetAsync(
            "/v1/injector/msfx/warehouse-success?warehouseBillNo=b1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Warehouse_success_returns_exists()
    {
        var injector = new FakeInjector { WarehouseExists = true };
        await using var factory = new ApiFactory { Injector = injector };
        using var client = await AuthorizeAsync(factory);

        using var response = await client.GetAsync(
            "/v1/injector/msfx/warehouse-success?warehouseBillNo=b1&drugId=d1&spec=s1&rowFingerprint=fp",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("b1", injector.LastBill);
        Assert.Equal("fp", injector.LastFingerprint);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task Pending_codes_returns_rows()
    {
        await using var factory = new ApiFactory { Injector = new FakeInjector() };
        using var client = await AuthorizeAsync(factory);

        using var response = await client.GetAsync(
            "/v1/injector/msfx/tasks/9/pending-codes",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal("L1", doc.RootElement[0].GetProperty("leafCode").GetString());
    }

    [Fact]
    public async Task Claim_returns_tasks()
    {
        await using var factory = new ApiFactory { Injector = new FakeInjector() };
        using var client = await AuthorizeAsync(factory);

        using var request = PostJson(
            "/v1/injector/msfx/claim",
            """{"clientId":"c1","drugId":"d1","spec":"s1"}""");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(7, doc.RootElement.GetProperty("tasks")[0].GetProperty("taskId").GetInt64());
    }

    private static async Task<HttpClient> AuthorizeAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage PostJson(string path, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        return request;
    }

    private sealed class FakeInjector : IInjectorService
    {
        public string? LastTxnId { get; private set; }

        public string? LastCommitTxnId { get; private set; }

        public string? LastRollbackTxnId { get; private set; }

        public int LastCleanupMinutes { get; private set; }

        public int LastCleanupBatch { get; private set; }

        public int CleanupCount { get; init; }

        public bool WarehouseExists { get; init; }

        public string? LastBill { get; private set; }

        public string? LastFingerprint { get; private set; }

        public Task<InjectorReserveResult> ReserveAsync(InjectorReserveRequest request, CancellationToken ct)
        {
            LastTxnId = request.TxnId;
            return Task.FromResult(new InjectorReserveResult(
                Ok: true,
                Reason: null,
                Message: null,
                Items: [new InjectorReserveItem(1, 1, "CODE1", 1)],
                SumTake: 1,
                WholeN: request.WholeN,
                RemNeed: request.RemNeed));
        }

        public Task<InjectorTxnResult> CommitAsync(string txnId, CancellationToken ct)
        {
            LastCommitTxnId = txnId;
            return Task.FromResult(new InjectorTxnResult(true, null, 0));
        }

        public Task<InjectorTxnResult> RollbackAsync(string txnId, CancellationToken ct)
        {
            LastRollbackTxnId = txnId;
            return Task.FromResult(new InjectorTxnResult(true, null, 1));
        }

        public Task<InjectorCleanupResult> CleanupPendingAsync(
            int timeoutMinutes,
            int maxBatch,
            CancellationToken ct)
        {
            LastCleanupMinutes = timeoutMinutes;
            LastCleanupBatch = maxBatch;
            return Task.FromResult(new InjectorCleanupResult(true, CleanupCount));
        }

        public Task<InjectorClaimResult> ClaimByTargetAsync(
            string clientId,
            string drugId,
            string spec,
            CancellationToken ct)
            => Task.FromResult(new InjectorClaimResult(
                [new InjectorClaimedTask(7, 8, "BILL", drugId, spec, 2)]));

        public Task<bool> HasWarehouseSuccessAsync(
            string warehouseBillNo,
            string drugId,
            string spec,
            string? rowFingerprint,
            CancellationToken ct)
        {
            LastBill = warehouseBillNo;
            LastFingerprint = rowFingerprint;
            return Task.FromResult(WarehouseExists);
        }

        public Task<IReadOnlyList<InjectorPendingCode>> GetPendingCodesAsync(long taskId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InjectorPendingCode>>(
                [new InjectorPendingCode(1, "L1", 11, "a", "", "", "", "")]);

        public Task UpdateTaskCodesAsync(
            long taskId,
            IReadOnlyList<string> leafCodes,
            string status,
            string? verifyResult,
            string? errMsg,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task UpdateStagingStatusAsync(
            IReadOnlyList<long> stagingIds,
            string codeStatus,
            string? errMsg,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task InsertEventAsync(
            long taskId,
            string stage,
            string level,
            string message,
            string? leafCode,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task<InjectorFinalizeResult> FinalizeAsync(
            long taskId,
            string? errMsg,
            string? warehouseBillNo,
            string? rowFingerprint,
            CancellationToken ct)
            => Task.FromResult(new InjectorFinalizeResult(
                [new InjectorFinalizeRow(taskId, "DONE", 1, 0, 1)]));
    }
}
