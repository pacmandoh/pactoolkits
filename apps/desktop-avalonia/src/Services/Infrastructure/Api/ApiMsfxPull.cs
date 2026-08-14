using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>AutoRun 拉取游标、批次与观察队列</summary>
public sealed class ApiMsfxPull : IMsfxPullRepo
{
    private static readonly TimeSpan RenewPeriod = TimeSpan.FromSeconds(15);

    private readonly PacApiClient _api;
    private readonly ApiMsfxRunLock _runLock;
    private readonly object _gate = new();
    private Guid? _lockId;
    private string? _lockSource;
    private CancellationTokenSource? _renewCts;
    private Task? _renewTask;

    public ApiMsfxPull(PacApiClient api, ApiMsfxRunLock runLock)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _runLock = runLock ?? throw new ArgumentNullException(nameof(runLock));
    }

    public async Task<IAsyncDisposable?> TryAcquireRunLockAsync(string sourceApi, CancellationToken ct)
    {
        var request = new MsfxRunLockRequest(sourceApi);
        var result = await PostJson(
                "/v1/msfx/autorun/lock",
                request,
                PacJsonContext.Default.MsfxRunLockRequest,
                PacJsonContext.Default.MsfxRunLockResult,
                ct)
            .ConfigureAwait(false);
        if (result is null || !result.Acquired || result.LockId is not { } lockId)
        {
            return null;
        }

        StartRenew(sourceApi, lockId);
        _runLock.Current = lockId;
        return new RunLock(this);
    }

    public async Task<int> FailInterruptedPullBatchesAsync(string sourceApi, string error, CancellationToken ct)
    {
        var body = new MsfxFailInterruptedRequest(sourceApi, error);
        var result = await PostJson(
                "/v1/msfx/autorun/fail-interrupted",
                body,
                PacJsonContext.Default.MsfxFailInterruptedRequest,
                PacJsonContext.Default.MsfxCountResult,
                ct)
            .ConfigureAwait(false);
        return result?.Count ?? 0;
    }

    public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
        => GetRequired(
            "/v1/msfx/autorun/window?sourceApi=" + Uri.EscapeDataString(sourceApi ?? string.Empty),
            PacJsonContext.Default.MsfxPullWindow,
            ct);

    public Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct)
        => GetRequired(
            "/v1/msfx/cursor?sourceApi=" + Uri.EscapeDataString(sourceApi ?? string.Empty),
            PacJsonContext.Default.MsfxPullCursorState,
            ct);

    public async Task<bool> AdvancePullCursorToAsync(
        string sourceApi,
        DateTimeOffset target,
        CancellationToken ct)
    {
        var request = new MsfxCursorAdvanceRequest(sourceApi, target);
        _ = await PostJson(
                "/v1/msfx/cursor/advance",
                request,
                PacJsonContext.Default.MsfxCursorAdvanceRequest,
                PacJsonContext.Default.MsfxPullCursorState,
                ct)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<MsfxPullBatchStartResult> StartPullBatchAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        CancellationToken ct)
        => await PostJson(
                   "/v1/msfx/autorun/batches/start",
                   new MsfxStartBatchRequest(sourceApi, beginAt, endAt),
                   PacJsonContext.Default.MsfxStartBatchRequest,
                   PacJsonContext.Default.MsfxPullBatchStartResult,
                   ct)
               .ConfigureAwait(false)
           ?? throw new InvalidOperationException("empty autorun batch start response");

    public Task FinishPullBatchAsync(
        long batchId,
        string status,
        int successCount,
        int failCount,
        string? errMsg,
        CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/batches/finish",
            new MsfxFinishBatchRequest(batchId, status, successCount, failCount, errMsg),
            PacJsonContext.Default.MsfxFinishBatchRequest,
            ct);

    public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/batches/request-id",
            new MsfxBatchRequestIdRequest(batchId, requestId),
            PacJsonContext.Default.MsfxBatchRequestIdRequest,
            ct);

    public Task AdvancePullCursorAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        long batchId,
        string batchStatus,
        CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/cursor/advance-window",
            new MsfxAdvanceWindowRequest(sourceApi, beginAt, endAt, batchId, batchStatus),
            PacJsonContext.Default.MsfxAdvanceWindowRequest,
            ct);

    public async Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct)
    {
        var path = "/v1/msfx/autorun/retries?sourceApi="
                   + Uri.EscapeDataString(sourceApi ?? string.Empty)
                   + "&limit="
                   + Uri.EscapeDataString(limit.ToString(CultureInfo.InvariantCulture));
        var rows = await GetJson(path, PacJsonContext.Default.MsfxBillRetryRowArray, ct).ConfigureAwait(false);
        return rows ?? Array.Empty<MsfxBillRetryRow>();
    }

    public Task UpsertBillRetryAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? lastError,
        CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/retries/upsert",
            new MsfxUpsertRetryRequest(sourceApi, billCode, fromRefUserId, toRefUserId, lastError),
            PacJsonContext.Default.MsfxUpsertRetryRequest,
            ct);

    public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/retries/succeeded",
            new MsfxBillCodeRequest(sourceApi, billCode),
            PacJsonContext.Default.MsfxBillCodeRequest,
            ct);

    public Task UpsertBillWatchAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? fromEntName,
        string? billType,
        string? billTime,
        string? billUploadTime,
        string? lastSeenStatus,
        string? rawJson,
        CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/watches/upsert",
            new MsfxUpsertWatchRequest(
                sourceApi,
                billCode,
                fromRefUserId,
                toRefUserId,
                fromEntName,
                billType,
                billTime,
                billUploadTime,
                lastSeenStatus,
                rawJson),
            PacJsonContext.Default.MsfxUpsertWatchRequest,
            ct);

    public async Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct)
    {
        var path = "/v1/msfx/autorun/watches?sourceApi="
                   + Uri.EscapeDataString(sourceApi ?? string.Empty)
                   + "&limit="
                   + Uri.EscapeDataString(limit.ToString(CultureInfo.InvariantCulture));
        var rows = await GetJson(path, PacJsonContext.Default.MsfxBillWatchRowArray, ct).ConfigureAwait(false);
        return rows ?? Array.Empty<MsfxBillWatchRow>();
    }

    public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/watches/resolved",
            new MsfxBillCodeRequest(sourceApi, billCode),
            PacJsonContext.Default.MsfxBillCodeRequest,
            ct);

    public Task RescheduleBillWatchAsync(
        string sourceApi,
        string billCode,
        string? lastSeenStatus,
        string? lastError,
        CancellationToken ct)
        => PostEmpty(
            "/v1/msfx/autorun/watches/reschedule",
            new MsfxRescheduleWatchRequest(sourceApi, billCode, lastSeenStatus, lastError),
            PacJsonContext.Default.MsfxRescheduleWatchRequest,
            ct);

    private void StartRenew(string sourceApi, Guid lockId)
    {
        lock (_gate)
        {
            _lockId = lockId;
            _lockSource = sourceApi;
            _renewCts = new CancellationTokenSource();
            var token = _renewCts.Token;
            _renewTask = RenewLoopAsync(sourceApi, lockId, token);
        }
    }

    private async Task RenewLoopAsync(string sourceApi, Guid lockId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RenewPeriod, ct).ConfigureAwait(false);
                await PostEmpty(
                        "/v1/msfx/autorun/renew",
                        new MsfxRunLockReleaseRequest(sourceApi, lockId),
                        PacJsonContext.Default.MsfxRunLockReleaseRequest,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 下次周期再续；释放时 Unlock 会清掉
            }
        }
    }

    private async Task UnlockAsync()
    {
        Guid lockId;
        string source;
        CancellationTokenSource? renewCts;
        Task? renewTask;
        lock (_gate)
        {
            if (_lockId is null || _lockSource is null)
            {
                return;
            }

            lockId = _lockId.Value;
            source = _lockSource;
            renewCts = _renewCts;
            renewTask = _renewTask;
            _lockId = null;
            _lockSource = null;
            _renewCts = null;
            _renewTask = null;
        }

        _runLock.Current = null;

        if (renewCts is not null)
        {
            await renewCts.CancelAsync().ConfigureAwait(false);
        }

        if (renewTask is not null)
        {
            try
            {
                await renewTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        renewCts?.Dispose();
        try
        {
            await PostEmpty(
                    "/v1/msfx/autorun/unlock",
                    new MsfxRunLockReleaseRequest(source, lockId),
                    PacJsonContext.Default.MsfxRunLockReleaseRequest,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // 解锁失败由 API 空闲超时回收
        }
    }

    private async Task<T> GetRequired<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken ct)
        where T : class
        => await GetJson(path, typeInfo, ct).ConfigureAwait(false)
           ?? throw new InvalidOperationException("empty autorun response");

    private Task<T?> GetJson<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken ct)
        => _api.GetJsonAsync(() => WithLock(new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path))), typeInfo, ct);

    private Task<T?> PostJson<TBody, T>(
        string path,
        TBody body,
        JsonTypeInfo<TBody> bodyInfo,
        JsonTypeInfo<T> resultInfo,
        CancellationToken ct)
        => _api.PostJsonAsync(
            () => WithLock(new HttpRequestMessage(HttpMethod.Post, _api.Resolve(path))
            {
                Content = JsonContent(body, bodyInfo),
            }),
            resultInfo,
            ct);

    private Task PostEmpty<TBody>(string path, TBody body, JsonTypeInfo<TBody> bodyInfo, CancellationToken ct)
        => _api.PostAsync(
            () => WithLock(new HttpRequestMessage(HttpMethod.Post, _api.Resolve(path))
            {
                Content = JsonContent(body, bodyInfo),
            }),
            ct);

    private HttpRequestMessage WithLock(HttpRequestMessage request)
    {
        _runLock.Apply(request);
        return request;
    }

    private static StringContent JsonContent<T>(T value, JsonTypeInfo<T> typeInfo)
        => new(JsonSerializer.Serialize(value, typeInfo), Encoding.UTF8, "application/json");

    private sealed class RunLock(ApiMsfxPull owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
            => new(owner.UnlockAsync());
    }
}
