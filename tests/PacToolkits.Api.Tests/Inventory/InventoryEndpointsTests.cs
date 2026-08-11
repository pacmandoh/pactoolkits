using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class InventoryEndpointsTests
{
    [Fact]
    public async Task EditStock_returns_batch_result()
    {
        var inventory = new FakeInventory();
        await using var factory = new ApiFactory { Inventory = inventory };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/stock/edit")
        {
            Content = new StringContent(
                """
                {
                  "edits": [
                    { "matchTraceCode": "T1", "expectedVersion": 1, "newTraceCode": null, "newRemain": 2 }
                  ],
                  "traceCodeRule": { "requiredLength": 2, "pattern": "" }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inventory.EditCalls);
        Assert.Contains("savedCount", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditStock_maps_format_error_to_400()
    {
        var inventory = new FakeInventory { ThrowArgumentOnEdit = true };
        await using var factory = new ApiFactory { Inventory = inventory };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/stock/edit")
        {
            Content = new StringContent(
                """
                {
                  "edits": [
                    { "matchTraceCode": "T1", "expectedVersion": 1, "newTraceCode": "bad", "newRemain": null }
                  ],
                  "traceCodeRule": { "requiredLength": 20, "pattern": "" }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, inventory.EditCalls);
    }

    [Fact]
    public async Task EditStock_returns_409_when_conflicts()
    {
        var inventory = new FakeInventory { NextEditHasConflict = true };
        await using var factory = new ApiFactory { Inventory = inventory };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/stock/edit")
        {
            Content = new StringContent(
                """
                {
                  "edits": [
                    { "matchTraceCode": "T1", "expectedVersion": 1, "newTraceCode": null, "newRemain": 2 }
                  ],
                  "traceCodeRule": { "requiredLength": 2, "pattern": "" }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"conflict\"", body, StringComparison.Ordinal);
        Assert.Contains("traceId", body, StringComparison.Ordinal);
        Assert.Contains("conflicts", body, StringComparison.Ordinal);
        Assert.Contains("T1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReassignCommit_rejects_non_positive_target_qty()
    {
        await using var factory = new ApiFactory { Inventory = new FakeInventory() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/reassign/commit")
        {
            Content = new StringContent(
                """
                {
                  "keyword": null,
                  "traceCodes": ["T1"],
                  "context": {
                    "targetDrugId": "d1",
                    "targetSpec": "s1",
                    "targetQty": 0,
                    "reason": "fix",
                    "operatorName": "op",
                    "source": "ui"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReassignCommit_rejects_null_context()
    {
        await using var factory = new ApiFactory { Inventory = new FakeInventory() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/reassign/commit")
        {
            Content = new StringContent(
                """
                {
                  "keyword": null,
                  "traceCodes": ["T1"],
                  "context": null
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteStock_rejects_null_trace_codes()
    {
        await using var factory = new ApiFactory { Inventory = new FakeInventory() };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/stock/delete")
        {
            Content = new StringContent("""{"traceCodes":null}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReassignCommit_by_trace_codes_calls_service()
    {
        var inventory = new FakeInventory();
        await using var factory = new ApiFactory { Inventory = inventory };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inventory/reassign/commit")
        {
            Content = new StringContent(
                """
                {
                  "keyword": null,
                  "traceCodes": ["T1", "T2"],
                  "context": {
                    "targetDrugId": "d1",
                    "targetSpec": "s1",
                    "targetQty": 1,
                    "reason": "fix",
                    "operatorName": "op",
                    "source": "ui"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inventory.ReassignTraceCalls);
        Assert.Equal(2, inventory.LastTraceCodes?.Count);
    }

    private sealed class FakeInventory : IInventoryOverviewService
    {
        public int EditCalls { get; private set; }

        public int ReassignTraceCalls { get; private set; }

        public IReadOnlyList<string>? LastTraceCodes { get; private set; }

        public bool NextEditHasConflict { get; init; }

        public bool ThrowArgumentOnEdit { get; init; }

        public int LargeBatchReassignConfirmThreshold => 100;

        public Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
            string? keyword, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new PagedResult<TracePoolStockRowDto>([], 0));

        public Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
            string? keyword, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new PagedResult<TracePoolDrugSpecAggDto>([], 0));

        public Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
            string? keyword, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new PagedResult<LowStockRowDto>([], 0));

        public Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
            string? keyword, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new PagedResult<MissingInventoryRowDto>([], 0));

        public Task<bool> TargetDrugSpecExistsAsync(string drugId, string spec, CancellationToken ct)
            => Task.FromResult(true);

        public Task<StockRowEditBatchResult> ApplyStockRowEditsAsync(
            IReadOnlyList<StockRowEditRequest> edits,
            TraceCodeValidationRule traceCodeRule,
            CancellationToken ct)
        {
            EditCalls++;
            if (ThrowArgumentOnEdit)
            {
                throw new ArgumentException("T1: 追溯码长度必须为 20 位");
            }

            if (NextEditHasConflict)
            {
                return Task.FromResult(new StockRowEditBatchResult(
                    0,
                    0,
                    null,
                    Array.Empty<StockRowEditSaved>(),
                    [
                        new StockRowEditConflict(
                            "T1",
                            null,
                            2,
                            new TracePoolStockRowDto(
                                DrugId: "d1",
                                Spec: "s1",
                                TraceCode: "T1",
                                Qty: 1,
                                Remain: 1,
                                Status: 0,
                                Version: 9,
                                IsLow: false)),
                    ]));
            }

            return Task.FromResult(new StockRowEditBatchResult(
                1,
                0,
                null,
                [new StockRowEditSaved("T1", 2, null, 2)],
                Array.Empty<StockRowEditConflict>()));
        }

        public Task<int> DeleteStockByTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct)
            => Task.FromResult(traceCodes.Count);

        public Task<StockReassignPreviewDto> PreviewReassignByKeywordAsync(
            string keyword,
            string targetDrugId,
            string targetSpec,
            int targetQty,
            int sampleLimit,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<StockReassignApplyResultDto> ReassignByTraceCodesAsync(
            IReadOnlyList<string> traceCodes,
            StockReassignContext context,
            CancellationToken ct)
        {
            if (context.TargetQty <= 0)
            {
                throw new ArgumentException("目标数量必须为大于 0 的整数", nameof(context));
            }

            if (string.IsNullOrWhiteSpace(context.TargetDrugId)
                || string.IsNullOrWhiteSpace(context.TargetSpec))
            {
                throw new ArgumentException("目标药品名与规格不能为空");
            }

            if (string.IsNullOrWhiteSpace(context.Reason))
            {
                throw new ArgumentException("迁移原因不能为空");
            }

            ReassignTraceCalls++;
            LastTraceCodes = traceCodes;
            return Task.FromResult(new StockReassignApplyResultDto(traceCodes.Count, 1));
        }

        public Task<StockReassignApplyResultDto> ReassignByKeywordAsync(
            string keyword,
            StockReassignContext context,
            CancellationToken ct)
            => throw new NotSupportedException();
    }
}
