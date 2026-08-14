using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services.Msfx;

namespace PacToolkits.Desktop.Tests;

public sealed class MsfxAutoRunPersistTests
{
    [Fact]
    public async Task TryHoldLock_requires_live_lock_for_source()
    {
        var clock = new ControllableClock();
        var pull = new FakePull();
        await using var persist = new MsfxAutoRunPersist(pull, new FakeIngest(), clock);
        var acquired = await persist.TryAcquireLockAsync("listupout", CancellationToken.None);
        Assert.True(acquired.Acquired);
        var lockId = acquired.LockId!.Value;

        Assert.True(persist.TryHoldLock(lockId, "listupout"));
        Assert.False(persist.TryHoldLock(Guid.NewGuid(), "listupout"));
        Assert.False(persist.TryHoldLock(lockId, "other"));
        Assert.True(persist.TryHoldLock(lockId, sourceApi: null));

        clock.Advance(TimeSpan.FromSeconds(46));
        Assert.False(persist.TryHoldLock(lockId, "listupout"));
    }

    [Fact]
    public async Task Hold_and_sweep_at_idle_boundary_never_double_lock()
    {
        var ct = CancellationToken.None;
        for (var i = 0; i < 200; i++)
        {
            var clock = new FlipClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var pull = new FakePull();
            await using var persist = new MsfxAutoRunPersist(pull, new FakeIngest(), clock);
            var first = await persist.TryAcquireLockAsync("listupout", ct);
            Assert.True(first.Acquired);
            var lockId = first.LockId!.Value;
            clock.ArmBoundary(TimeSpan.FromSeconds(44), TimeSpan.FromSeconds(46));

            var hold = Task.Run(() => persist.TryHoldLock(lockId, "listupout"), ct);
            var sweep = persist.SweepExpiredAsync();
            await Task.WhenAll(hold, sweep);

            var held = await hold;
            var second = await persist.TryAcquireLockAsync("listupout", ct);
            Assert.False(held && second.Acquired, "live hold must not overlap a second lock");
            if (held)
            {
                Assert.True(await persist.RenewLockAsync("listupout", lockId, ct));
                Assert.Equal(0, pull.Sessions[0].Disposes);
            }
        }
    }

    [Fact]
    public async Task Sweep_does_not_drop_newer_lock()
    {
        var ct = CancellationToken.None;
        for (var i = 0; i < 80; i++)
        {
            var clock = new ControllableClock();
            var pull = new FakePull();
            await using var persist = new MsfxAutoRunPersist(pull, new FakeIngest(), clock);
            var first = await persist.TryAcquireLockAsync("listupout", ct);
            Assert.True(first.Acquired);

            clock.Advance(TimeSpan.FromSeconds(46));
            var acquire = persist.TryAcquireLockAsync("listupout", ct);
            var sweep = persist.SweepExpiredAsync();
            await Task.WhenAll(acquire, sweep);

            var second = await acquire;
            Assert.True(second.Acquired);
            Assert.NotNull(second.LockId);
            Assert.NotEqual(first.LockId, second.LockId);
            Assert.True(await persist.RenewLockAsync("listupout", second.LockId.Value, ct));
            Assert.Equal(0, pull.Sessions[^1].Disposes);
        }
    }

    private sealed class FlipClock : TimeProvider
    {
        private DateTimeOffset _now;
        private DateTimeOffset _late;
        private int _armedReads;
        private int _armed;

        public FlipClock(DateTimeOffset start) => _now = start;

        public void ArmBoundary(TimeSpan early, TimeSpan late)
        {
            var start = _now;
            _now = start + early;
            _late = start + late;
            Volatile.Write(ref _armedReads, 0);
            Volatile.Write(ref _armed, 1);
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (Volatile.Read(ref _armed) == 0)
            {
                return _now;
            }

            var n = Interlocked.Increment(ref _armedReads);
            return n == 1 ? _now : _late;
        }
    }

    private sealed class ControllableClock : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utc;

        public void Advance(TimeSpan span) => _utc += span;
    }

    private sealed class FakeSession : IAsyncDisposable
    {
        public int Disposes { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposes++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePull : IMsfxPullRepo
    {
        public List<FakeSession> Sessions { get; } = [];

        public Task<IAsyncDisposable?> TryAcquireRunLockAsync(string sourceApi, CancellationToken ct)
        {
            var session = new FakeSession();
            Sessions.Add(session);
            return Task.FromResult<IAsyncDisposable?>(session);
        }

        public Task<int> FailInterruptedPullBatchesAsync(string sourceApi, string error, CancellationToken ct)
            => Task.FromResult(0);

        public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> AdvancePullCursorToAsync(string sourceApi, DateTimeOffset target, CancellationToken ct)
            => Task.FromResult(false);

        public Task<MsfxPullBatchStartResult> StartPullBatchAsync(
            string sourceApi,
            DateTimeOffset beginAt,
            DateTimeOffset endAt,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task FinishPullBatchAsync(
            long batchId,
            string status,
            int successCount,
            int failCount,
            string? errMsg,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
            => Task.CompletedTask;

        public Task AdvancePullCursorAsync(
            string sourceApi,
            DateTimeOffset beginAt,
            DateTimeOffset endAt,
            long batchId,
            string batchStatus,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
            string sourceApi,
            int limit,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxBillRetryRow>>([]);

        public Task UpsertBillRetryAsync(
            string sourceApi,
            string billCode,
            string? fromRefUserId,
            string? toRefUserId,
            string? lastError,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
            => Task.CompletedTask;

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
            => Task.CompletedTask;

        public Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(
            string sourceApi,
            int limit,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxBillWatchRow>>([]);

        public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
            => Task.CompletedTask;

        public Task RescheduleBillWatchAsync(
            string sourceApi,
            string billCode,
            string? lastSeenStatus,
            string? lastError,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakeIngest : IMsfxIngestRepo
    {
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
            => Task.FromResult(0L);

        public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
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
            => throw new NotSupportedException();
    }
}
