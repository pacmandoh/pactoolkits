using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class InjectorServiceTests
{
    [Fact]
    public async Task Reserve_zero_need_skips_repo()
    {
        var repo = new FakeRepo();
        var sut = new InjectorService(repo);

        var result = await sut.ReserveAsync(
            new InjectorReserveRequest("t1", "c1", "d1", "s1", 0, 0),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("SKIP", result.Reason);
        Assert.Equal(0, repo.ReserveCalls);
    }

    [Fact]
    public async Task Reserve_rejects_negative_counts()
    {
        var sut = new InjectorService(new FakeRepo());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.ReserveAsync(
                new InjectorReserveRequest("t1", "c1", "d1", "s1", -1, 0),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reserve_rejects_blank_txn()
    {
        var sut = new InjectorService(new FakeRepo());

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReserveAsync(
                new InjectorReserveRequest(" ", "c1", "d1", "s1", 0, 1),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_clamps_and_counts_successful_rollbacks()
    {
        var repo = new FakeRepo
        {
            ExpiredIds = ["ok-1", "fail-2", "ok-3"],
        };
        var sut = new InjectorService(repo);

        var result = await sut.CleanupPendingAsync(0, 9999, TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Cleaned);
        Assert.Equal(1, repo.LastTimeoutMinutes);
        Assert.Equal(500, repo.LastMaxBatch);
        Assert.Equal(["ok-1", "fail-2", "ok-3"], repo.RolledBack);
    }

    private sealed class FakeRepo : IInjectorRepo
    {
        public int ReserveCalls { get; private set; }

        public IReadOnlyList<string> ExpiredIds { get; init; } = [];

        public int LastTimeoutMinutes { get; private set; }

        public int LastMaxBatch { get; private set; }

        public List<string> RolledBack { get; } = [];

        public Task<InjectorReserveResult> ReserveAsync(InjectorReserveRequest request, CancellationToken ct)
        {
            ReserveCalls++;
            return Task.FromResult(new InjectorReserveResult(
                true, null, null, [], 0, request.WholeN, request.RemNeed));
        }

        public Task<InjectorTxnResult> CommitAsync(string txnId, CancellationToken ct)
            => Task.FromResult(new InjectorTxnResult(true, null, 0));

        public Task<InjectorTxnResult> RollbackAsync(string txnId, CancellationToken ct)
        {
            RolledBack.Add(txnId);
            var ok = !txnId.StartsWith("fail", StringComparison.Ordinal);
            return Task.FromResult(new InjectorTxnResult(ok, ok ? null : "fail", ok ? 1 : 0));
        }

        public Task<IReadOnlyList<string>> ListExpiredPendingTxnIdsAsync(
            int timeoutMinutes,
            int maxBatch,
            CancellationToken ct)
        {
            LastTimeoutMinutes = timeoutMinutes;
            LastMaxBatch = maxBatch;
            return Task.FromResult(ExpiredIds);
        }

        public Task<IReadOnlyList<InjectorClaimedTask>> ClaimByTargetAsync(
            string clientId,
            string drugId,
            string spec,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InjectorClaimedTask>>([]);

        public Task<bool> HasWarehouseSuccessAsync(
            string warehouseBillNo,
            string drugId,
            string spec,
            string? rowFingerprint,
            CancellationToken ct)
            => Task.FromResult(false);

        public Task<IReadOnlyList<InjectorPendingCode>> GetPendingCodesAsync(long taskId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InjectorPendingCode>>([]);

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

        public Task<IReadOnlyList<InjectorFinalizeRow>> FinalizeAsync(
            long taskId,
            string? errMsg,
            string? warehouseBillNo,
            string? rowFingerprint,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InjectorFinalizeRow>>([]);
    }
}
