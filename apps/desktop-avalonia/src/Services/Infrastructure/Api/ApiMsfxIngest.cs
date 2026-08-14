using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>AutoRun 单据入库</summary>
public sealed class ApiMsfxIngest : IMsfxIngestRepo
{
    private readonly PacApiClient _api;
    private readonly ApiMsfxRunLock _runLock;

    public ApiMsfxIngest(PacApiClient api, ApiMsfxRunLock runLock)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _runLock = runLock ?? throw new ArgumentNullException(nameof(runLock));
    }

    public async Task<long> UpsertInboundBillAsync(
        long batchId,
        string billCode,
        string billType,
        string billTime,
        string billUploadTime,
        string fromRefUserId,
        string fromEntName,
        string toRefUserId,
        string status,
        string rawJson,
        CancellationToken ct)
    {
        var request = new MsfxUpsertBillRequest(
            batchId,
            billCode,
            billType,
            billTime,
            billUploadTime,
            fromRefUserId,
            fromEntName,
            toRefUserId,
            status,
            rawJson);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxUpsertBillRequest);
        var result = await _api.PostJsonAsync(
                           () =>
                           {
                               var message = new HttpRequestMessage(
                                   HttpMethod.Post,
                                   _api.Resolve("/v1/msfx/autorun/bills/upsert"))
                               {
                                   Content = new StringContent(json, Encoding.UTF8, "application/json"),
                               };
                               _runLock.Apply(message);
                               return message;
                           },
                           PacJsonContext.Default.MsfxUpsertBillResult,
                           ct)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("empty autorun bill upsert response");
        return result.BillId;
    }

    public async Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<(
            string DrugName,
            string PackageSpec,
            string PrepnSpec,
            string BatchNo,
            IReadOnlyList<(
                string Code,
                string CodeLevel,
                string? Level1Code,
                string? Level2Code,
                string? Level3Code,
                string? Level4Code,
                string? Level5Code)> Codes)> drugs,
        CancellationToken ct)
    {
        var request = new MsfxIngestRequest(
            billId,
            billCode,
            drugs.Select(drug => new MsfxIngestDrugDto(
                    drug.DrugName,
                    drug.PackageSpec,
                    drug.PrepnSpec,
                    drug.BatchNo,
                    drug.Codes.Select(code => new MsfxIngestCodeDto(
                            code.Code,
                            code.CodeLevel,
                            code.Level1Code,
                            code.Level2Code,
                            code.Level3Code,
                            code.Level4Code,
                            code.Level5Code))
                        .ToList()))
                .ToList());
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxIngestRequest);
        return await _api.PostJsonAsync(
                   () =>
                   {
                       var message = new HttpRequestMessage(
                           HttpMethod.Post,
                           _api.Resolve("/v1/msfx/autorun/bills/ingest"))
                       {
                           Content = new StringContent(json, Encoding.UTF8, "application/json"),
                       };
                       _runLock.Apply(message);
                       return message;
                   },
                   PacJsonContext.Default.MsfxIngestDetailResult,
                   ct)
               .ConfigureAwait(false)
           ?? throw new InvalidOperationException("empty autorun ingest response");
    }
}
