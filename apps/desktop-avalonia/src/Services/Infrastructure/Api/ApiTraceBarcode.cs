using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>追溯码条码取码与审计 HTTP 适配</summary>
public sealed class ApiTraceBarcode : ITraceBarcodeService
{
    private readonly PacApiClient _api;

    public event Action? AuditCleared;

    public ApiTraceBarcode(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new TraceBarcodePickApiRequest(
            request.BatchId,
            request.OperatorName,
            request.Items,
            request.ExcludeRecentDays,
            request.ExcludeTraceCodes);
        var json = JsonSerializer.Serialize(body, PacJsonContext.Default.TraceBarcodePickApiRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/trace-barcodes/pick"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.TraceBarcodePickResult,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty trace-barcodes pick response");
    }

    public async Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new TraceBarcodeAuditApiRequest(
            request.BatchId,
            request.Action,
            request.OperatorName,
            request.Items);
        var json = JsonSerializer.Serialize(body, PacJsonContext.Default.TraceBarcodeAuditApiRequest);
        var response = await _api.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/trace-barcodes/audit"))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                },
                PacJsonContext.Default.TraceBarcodeAuditApiResponse,
                ct,
                request.CommandId)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty trace-barcodes audit response");
        return response.Inserted;
    }

    public async Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null)
    {
        var response = await _api.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/trace-barcodes/audit-log/clear"))
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                },
                PacJsonContext.Default.TraceBarcodeClearAuditApiResponse,
                ct,
                commandId)
            .ConfigureAwait(true)
            ?? throw new InvalidOperationException("empty trace-barcodes clear response");
        AuditCleared?.Invoke();
        return response.Deleted;
    }
}
