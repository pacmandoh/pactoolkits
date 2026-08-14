using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>库存走 HTTP</summary>
public sealed class ApiInventory : IInventoryOverviewService
{
    private readonly PacApiClient _api;

    public ApiInventory(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public int LargeBatchReassignConfirmThreshold
        => InventoryOverviewService.DefaultLargeBatchReassignConfirmThreshold;

    public Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync("/v1/inventory/stock", keyword, page, pageSize, PacJsonContext.Default.PagedResultTracePoolStockRowDto, ct);

    public Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/inventory/drug-spec",
            keyword,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultTracePoolDrugSpecAggDto,
            ct);

    public Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/inventory/low-stock",
            keyword,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultLowStockRowDto,
            ct);

    public Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/inventory/missing",
            keyword,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultMissingInventoryRowDto,
            ct);

    public async Task<bool> TargetDrugSpecExistsAsync(string drugId, string spec, CancellationToken ct)
    {
        var drug = InputNormalizer.Normalize(drugId)
                   ?? throw new ArgumentException("drugId is required", nameof(drugId));
        var specKey = InputNormalizer.Normalize(spec)
                      ?? throw new ArgumentException("spec is required", nameof(spec));
        var uri = _api.Resolve(
            "/v1/inventory/target-exists?drugId="
            + Uri.EscapeDataString(drug)
            + "&spec="
            + Uri.EscapeDataString(specKey));
        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, uri),
                PacJsonContext.Default.InventoryTargetExistsResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty inventory target-exists response");
        return body.Exists;
    }

    public async Task<StockRowEditBatchResult> ApplyStockRowEditsAsync(
        IReadOnlyList<StockRowEditRequest> edits,
        TraceCodeValidationRule traceCodeRule,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(traceCodeRule);
        var request = new InventoryStockEditRequest(edits, traceCodeRule);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.InventoryStockEditRequest);
        try
        {
            return await _api.PostJsonAsync(
                       () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/inventory/stock/edit"))
                       {
                           Content = new StringContent(json, Encoding.UTF8, "application/json"),
                       },
                       PacJsonContext.Default.StockRowEditBatchResult,
                       ct)
                   .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("empty inventory stock edit response");
        }
        catch (PacApiConflictException ex) when (
            string.Equals(ex.Code, "conflict", StringComparison.Ordinal)
            && ex.Problem.Conflicts is { Count: > 0 })
        {
            // conflicts 保持批结果形态，供放弃或强制覆盖
            var lastError = string.IsNullOrWhiteSpace(ex.Problem.Detail)
                ? ex.Message
                : ex.Problem.Detail;
            return new StockRowEditBatchResult(
                0,
                0,
                lastError,
                Array.Empty<StockRowEditSaved>(),
                ex.Problem.Conflicts);
        }
    }

    public async Task<int> DeleteStockByTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(traceCodes);
        var request = new InventoryStockDeleteRequest(traceCodes);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.InventoryStockDeleteRequest);
        var body = await _api.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/inventory/stock/delete"))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                },
                PacJsonContext.Default.InventoryStockDeleteResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty inventory stock delete response");
        return body.DeletedCount;
    }

    public async Task<StockReassignPreviewDto> PreviewReassignByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct)
    {
        var request = new InventoryReassignPreviewRequest(
            keyword,
            targetDrugId,
            targetSpec,
            targetQty,
            sampleLimit);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.InventoryReassignPreviewRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/inventory/reassign/preview"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.StockReassignPreviewDto,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty inventory reassign preview response");
    }

    public Task<StockReassignApplyResultDto> ReassignByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        StockReassignContext context,
        CancellationToken ct)
        => CommitReassignAsync(
            new InventoryReassignCommitRequest(Keyword: null, TraceCodes: traceCodes, Context: context),
            ct);

    public Task<StockReassignApplyResultDto> ReassignByKeywordAsync(
        string keyword,
        StockReassignContext context,
        CancellationToken ct)
        => CommitReassignAsync(
            new InventoryReassignCommitRequest(Keyword: keyword, TraceCodes: null, Context: context),
            ct);

    private async Task<StockReassignApplyResultDto> CommitReassignAsync(
        InventoryReassignCommitRequest request,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.InventoryReassignCommitRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/inventory/reassign/commit"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.StockReassignApplyResultDto,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty inventory reassign commit response");
    }

    private async Task<PagedResult<T>> GetPageAsync<T>(
        string path,
        string? keyword,
        int page,
        int pageSize,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<PagedResult<T>> typeInfo,
        CancellationToken ct)
    {
        var query = new List<string>
        {
            "page=" + Uri.EscapeDataString(page.ToString(CultureInfo.InvariantCulture)),
            "pageSize=" + Uri.EscapeDataString(pageSize.ToString(CultureInfo.InvariantCulture)),
        };
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query.Add("keyword=" + Uri.EscapeDataString(keyword));
        }

        return await _api.GetJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path + "?" + string.Join('&', query))),
                   typeInfo,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException($"empty inventory page response for {path}");
    }
}
