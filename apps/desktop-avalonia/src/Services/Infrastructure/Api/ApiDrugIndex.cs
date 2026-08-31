using System;
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

/// <summary>药品索引走 HTTP</summary>
public sealed class ApiDrugIndex : IDrugIndexService
{
    private readonly PacApiClient _api;

    public ApiDrugIndex(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<DrugIndexSearchResult> SearchAsync(string? keyword, int limit, CancellationToken ct)
    {
        var query = "limit=" + Uri.EscapeDataString(limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query += "&keyword=" + Uri.EscapeDataString(keyword.Trim());
        }

        return await _api.GetJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/drugs?" + query)),
                   PacJsonContext.Default.DrugIndexSearchResult,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty drugs search response");
    }

    public async Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
    {
        var drug = InputNormalizer.Normalize(drugId)
                   ?? throw new ArgumentException("drugId is required", nameof(drugId));
        var specKey = InputNormalizer.Normalize(spec)
                      ?? throw new ArgumentException("spec is required", nameof(spec));
        try
        {
            return await _api.GetJsonAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, KeyUri(drug, specKey)),
                    PacJsonContext.Default.DrugIndexDto,
                    ct)
                .ConfigureAwait(false);
        }
        catch (PacApiException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<DrugIndexSaveResult> SaveAsync(DrugIndexSaveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var drug = InputNormalizer.Normalize(request.Dto.DrugId)
                   ?? throw new ArgumentException("Dto.DrugId is required", nameof(request));
        var spec = InputNormalizer.Normalize(request.Dto.Spec)
                   ?? throw new ArgumentException("Dto.Spec is required", nameof(request));
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.DrugIndexSaveRequest);
        try
        {
            var body = await _api.PutJsonAsync(
                    () => new HttpRequestMessage(HttpMethod.Put, KeyUri(drug, spec))
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    },
                    PacJsonContext.Default.DrugIndexSaveResponse,
                    ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("empty drugs save response");

            return new DrugIndexSaveResult(body.Outcome, body.Saved, Concurrency: null);
        }
        catch (PacApiConflictException ex) when (string.Equals(ex.Code, "conflict", StringComparison.Ordinal))
        {
            var current = await GetByKeyAsync(drug, spec, ct).ConfigureAwait(false);
            var concurrency = new DrugIndexConcurrencyException(
                "该记录已被其他终端修改，请刷新后重试",
                current);
            return new DrugIndexSaveResult(DrugSaveOutcome.ConcurrencyConflict, current, concurrency);
        }
    }

    public async Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct)
    {
        var drug = InputNormalizer.Normalize(drugId)
                   ?? throw new ArgumentException("drugId is required", nameof(drugId));
        var specKey = InputNormalizer.Normalize(spec)
                      ?? throw new ArgumentException("spec is required", nameof(spec));
        var uri = KeyUri(drug, specKey, expectedVersion);
        try
        {
            await _api.DeleteAsync(
                    () => new HttpRequestMessage(HttpMethod.Delete, uri),
                    ct)
                .ConfigureAwait(false);
        }
        catch (PacApiConflictException ex) when (string.Equals(ex.Code, "conflict", StringComparison.Ordinal))
        {
            if (ex.CurrentVersion is not null)
            {
                var current = await GetByKeyAsync(drug, specKey, ct).ConfigureAwait(false);
                throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试", current);
            }

            throw new DrugIndexInUseException(ex);
        }
    }

    public async Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct)
    {
        var request = new DrugKeyFixPreviewRequest(sourceDrugId, sourceSpec, targetDrugId, targetSpec);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.DrugKeyFixPreviewRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/drugs/key-fix/preview"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.DrugKeyFixPreviewDto,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty key-fix preview response");
    }

    public async Task<DrugKeyFixCommitResult> ApplyKeyFixAsync(DrugKeyFixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.DrugKeyFixRequest);
        try
        {
            return await _api.PostJsonAsync(
                       () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/drugs/key-fix/commit"))
                       {
                           Content = new StringContent(json, Encoding.UTF8, "application/json"),
                       },
                       PacJsonContext.Default.DrugKeyFixCommitResult,
                       ct)
                   .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("empty key-fix commit response");
        }
        catch (PacApiConflictException ex) when (string.Equals(ex.Code, "conflict", StringComparison.Ordinal))
        {
            throw new DrugIndexConcurrencyException("该记录已被其他终端修改，请刷新后重试");
        }
    }

    private Uri KeyUri(string drug, string spec, long? expectedVersion = null)
    {
        var query = "drugId=" + Uri.EscapeDataString(drug) + "&spec=" + Uri.EscapeDataString(spec);
        if (expectedVersion is { } version)
        {
            query += "&expectedVersion="
                     + Uri.EscapeDataString(
                         version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return _api.Resolve("/v1/drugs/key?" + query);
    }
}
