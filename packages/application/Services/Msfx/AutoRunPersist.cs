using System.Collections.Concurrent;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

/// <summary>
/// AutoRun 入库：委托 pull/ingest；跑锁由 pull 持有，业务请求续期
/// </summary>
public sealed class MsfxAutoRunPersist : IMsfxAutoRunPersist, IAsyncDisposable
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(45);

    private readonly IMsfxPullRepo _pull;
    private readonly IMsfxIngestRepo _ingest;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Held> _bySource = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Held> _byId = new();
    private readonly Timer _sweep;

    private sealed class Held
    {
        public required string SourceApi { get; init; }
        public required Guid LockId { get; init; }
        public required IAsyncDisposable Session { get; init; }
        public long LastRenewUtcTicks;
        public bool Released;
    }

    public MsfxAutoRunPersist(IMsfxPullRepo pull, IMsfxIngestRepo ingest)
        : this(pull, ingest, TimeProvider.System)
    {
    }

    internal MsfxAutoRunPersist(IMsfxPullRepo pull, IMsfxIngestRepo ingest, TimeProvider clock)
    {
        _pull = pull ?? throw new ArgumentNullException(nameof(pull));
        _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sweep = new Timer(Sweep, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public async Task<MsfxRunLockResult> TryAcquireLockAsync(string sourceApi, CancellationToken ct)
    {
        var key = NormalizeSource(sourceApi);
        await ReleaseExpiredAsync(key).ConfigureAwait(false);
        if (_bySource.ContainsKey(key))
        {
            return new MsfxRunLockResult(false, null);
        }

        var session = await _pull.TryAcquireRunLockAsync(key, ct).ConfigureAwait(false);
        if (session is null)
        {
            return new MsfxRunLockResult(false, null);
        }

        var held = new Held
        {
            SourceApi = key,
            LockId = Guid.NewGuid(),
            Session = session,
            LastRenewUtcTicks = _clock.GetUtcNow().UtcTicks,
        };
        if (!_bySource.TryAdd(key, held))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return new MsfxRunLockResult(false, null);
        }

        _byId[held.LockId] = held;
        return new MsfxRunLockResult(true, held.LockId);
    }

    public async Task ReleaseLockAsync(string sourceApi, Guid lockId, CancellationToken ct)
    {
        _ = ct;
        var key = NormalizeSource(sourceApi);
        if (!_bySource.TryGetValue(key, out var held) || held.LockId != lockId)
        {
            return;
        }

        if (TryMarkReleased(held, expiredOnly: false))
        {
            await DropAsync(held).ConfigureAwait(false);
        }
    }

    public async Task<bool> RenewLockAsync(string sourceApi, Guid lockId, CancellationToken ct)
    {
        _ = ct;
        var key = NormalizeSource(sourceApi);
        if (!_bySource.TryGetValue(key, out var held) || held.LockId != lockId)
        {
            return false;
        }

        if (TryTouch(held, key))
        {
            return true;
        }

        if (TryMarkReleased(held, expiredOnly: true))
        {
            await DropAsync(held).ConfigureAwait(false);
        }

        return false;
    }

    public bool TryHoldLock(Guid lockId, string? sourceApi)
    {
        if (lockId == Guid.Empty || !_byId.TryGetValue(lockId, out var held))
        {
            return false;
        }

        return TryTouch(held, sourceApi);
    }

    public Task<int> FailInterruptedPullBatchesAsync(string sourceApi, string error, CancellationToken ct)
        => _pull.FailInterruptedPullBatchesAsync(NormalizeSource(sourceApi), error, ct);

    public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
        => _pull.GetPullWindowAsync(NormalizeSource(sourceApi), ct);

    public Task<MsfxPullBatchStartResult> StartPullBatchAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        CancellationToken ct)
        => _pull.StartPullBatchAsync(NormalizeSource(sourceApi), beginAt, endAt, ct);

    public Task FinishPullBatchAsync(
        long batchId,
        string status,
        int successCount,
        int failCount,
        string? errMsg,
        CancellationToken ct)
        => _pull.FinishPullBatchAsync(batchId, status, successCount, failCount, errMsg, ct);

    public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
        => _pull.UpdatePullBatchRequestIdAsync(batchId, requestId, ct);

    public Task AdvancePullCursorAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        long batchId,
        string batchStatus,
        CancellationToken ct)
        => _pull.AdvancePullCursorAsync(NormalizeSource(sourceApi), beginAt, endAt, batchId, batchStatus, ct);

    public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct)
        => _pull.GetDueBillRetriesAsync(NormalizeSource(sourceApi), limit, ct);

    public Task UpsertBillRetryAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? lastError,
        CancellationToken ct)
        => _pull.UpsertBillRetryAsync(NormalizeSource(sourceApi), billCode, fromRefUserId, toRefUserId, lastError, ct);

    public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
        => _pull.MarkBillRetrySucceededAsync(NormalizeSource(sourceApi), billCode, ct);

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
        => _pull.UpsertBillWatchAsync(
            NormalizeSource(sourceApi),
            billCode,
            fromRefUserId,
            toRefUserId,
            fromEntName,
            billType,
            billTime,
            billUploadTime,
            lastSeenStatus,
            rawJson,
            ct);

    public Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct)
        => _pull.GetDueBillWatchesAsync(NormalizeSource(sourceApi), limit, ct);

    public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
        => _pull.MarkBillWatchResolvedAsync(NormalizeSource(sourceApi), billCode, ct);

    public Task RescheduleBillWatchAsync(
        string sourceApi,
        string billCode,
        string? lastSeenStatus,
        string? lastError,
        CancellationToken ct)
        => _pull.RescheduleBillWatchAsync(NormalizeSource(sourceApi), billCode, lastSeenStatus, lastError, ct);

    public Task<long> UpsertInboundBillAsync(
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
        => _ingest.UpsertInboundBillAsync(
            batchId,
            billCode,
            billType,
            billTime,
            billUploadTime,
            fromRefUserId,
            fromEntName,
            toRefUserId,
            status,
            rawJson,
            ct);

    public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<MsfxIngestDrugDto> drugs,
        CancellationToken ct)
    {
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
                string? Level5Code)> Codes)> mapped = (drugs ?? [])
            .Select(drug => (
                drug.DrugName,
                drug.PackageSpec,
                drug.PrepnSpec,
                drug.BatchNo,
                (IReadOnlyList<(
                    string Code,
                    string CodeLevel,
                    string? Level1Code,
                    string? Level2Code,
                    string? Level3Code,
                    string? Level4Code,
                    string? Level5Code)>)(drug.Codes ?? [])
                    .Select(code => (
                        code.Code,
                        code.CodeLevel,
                        code.Level1Code,
                        code.Level2Code,
                        code.Level3Code,
                        code.Level4Code,
                        code.Level5Code))
                    .ToList()))
            .ToList();
        return _ingest.IngestUpoutDetailAsync(billId, billCode, mapped, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _sweep.DisposeAsync().ConfigureAwait(false);
        foreach (var held in _byId.Values.ToArray())
        {
            TryMarkReleased(held, expiredOnly: false);
            await DropAsync(held).ConfigureAwait(false);
        }
    }

    private void Sweep(object? _)
        => _ = SweepExpiredAsync();

    internal async Task SweepExpiredAsync()
    {
        foreach (var held in _byId.Values.ToArray())
        {
            if (TryMarkReleased(held, expiredOnly: true))
            {
                await DropAsync(held).ConfigureAwait(false);
            }
        }
    }

    private async Task ReleaseExpiredAsync(string key)
    {
        if (_bySource.TryGetValue(key, out var held) && TryMarkReleased(held, expiredOnly: true))
        {
            await DropAsync(held).ConfigureAwait(false);
        }
    }

    // 续期与过期释放同一把锁：先 Hold 成功再被 Sweep 删掉会让第二个 AutoRun 同时拿到新锁
    private bool TryTouch(Held held, string? sourceApi)
    {
        lock (held)
        {
            if (held.Released || IsExpired(held))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sourceApi)
                && !string.Equals(held.SourceApi, NormalizeSource(sourceApi), StringComparison.Ordinal))
            {
                return false;
            }

            Volatile.Write(ref held.LastRenewUtcTicks, _clock.GetUtcNow().UtcTicks);
            return true;
        }
    }

    private bool TryMarkReleased(Held held, bool expiredOnly)
    {
        lock (held)
        {
            if (held.Released)
            {
                return false;
            }

            if (expiredOnly && !IsExpired(held))
            {
                return false;
            }

            held.Released = true;
            return true;
        }
    }

    private async Task DropAsync(Held held)
    {
        var removedId = _byId.TryRemove(held.LockId, out _);
        var removedSource = _bySource.TryRemove(KeyValuePair.Create(held.SourceApi, held));
        if (!removedId && !removedSource)
        {
            return;
        }

        await held.Session.DisposeAsync().ConfigureAwait(false);
    }

    private bool IsExpired(Held held)
    {
        var last = new DateTimeOffset(Volatile.Read(ref held.LastRenewUtcTicks), TimeSpan.Zero);
        return _clock.GetUtcNow() - last > Idle;
    }

    private static string NormalizeSource(string sourceApi)
    {
        var key = (sourceApi ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0)
        {
            throw new ArgumentException("sourceApi is required", nameof(sourceApi));
        }

        return key;
    }
}
