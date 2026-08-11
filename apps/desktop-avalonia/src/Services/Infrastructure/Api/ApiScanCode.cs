using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>经 PacApi 的扫码入库</summary>
public sealed class ApiScanCode : IScanCodeService
{
    private readonly PacApiClient _api;

    public ApiScanCode(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<ScanCodeSubmitResult> SubmitAsync(ScanCodeSubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.ScanCodeSubmitRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/trace-codes/submit"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.ScanCodeSubmitResult,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty trace-codes submit response");
    }

    public async Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(traceCodes);
        var request = new TraceCodesExistingRequest(traceCodes);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.TraceCodesExistingRequest);
        var body = await _api.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/trace-codes/check-existing"))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                },
                PacJsonContext.Default.TraceCodesExistingResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty trace-codes check-existing response");
        return body.Existing;
    }
}
