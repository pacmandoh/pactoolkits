using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class MsfxAutoRunEndpointsTests
{
    [Fact]
    public async Task Lock_returns_acquired()
    {
        var persist = new FakePersist();
        await using var factory = new ApiFactory { AutoRunPersist = persist };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/autorun/lock")
        {
            Content = new StringContent("""{"sourceApi":"listupout"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, persist.LockCalls);
        Assert.Equal("listupout", persist.LastSourceApi);
    }

    [Fact]
    public async Task Unlock_releases_lock()
    {
        var persist = new FakePersist();
        var lockId = persist.SeedLock("listupout");
        await using var factory = new ApiFactory { AutoRunPersist = persist };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/autorun/unlock")
        {
            Content = new StringContent(
                $$"""{"sourceApi":"listupout","lockId":"{{lockId:D}}"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, persist.UnlockCalls);
    }

    [Fact]
    public async Task Window_without_lock_returns_409()
    {
        var persist = new FakePersist();
        persist.SeedLock("listupout");
        await using var factory = new ApiFactory { AutoRunPersist = persist };
        using var client = factory.CreateClient();
        await AuthorizeAsync(client);

        using var response = await client.GetAsync(
            "/v1/msfx/autorun/window?sourceApi=listupout",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, persist.HoldCalls);
    }

    [Fact]
    public async Task Window_with_foreign_lock_returns_409()
    {
        var persist = new FakePersist();
        var lockId = persist.SeedLock("other");
        await using var factory = new ApiFactory { AutoRunPersist = persist };
        using var client = factory.CreateClient();
        await AuthorizeAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/v1/msfx/autorun/window?sourceApi=listupout");
        request.Headers.TryAddWithoutValidation(PacApiHeaders.MsfxRunLock, lockId.ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, persist.HoldCalls);
    }

    [Fact]
    public async Task Window_with_held_lock_returns_ok()
    {
        var persist = new FakePersist();
        var lockId = persist.SeedLock("listupout");
        await using var factory = new ApiFactory { AutoRunPersist = persist };
        using var client = factory.CreateClient();
        await AuthorizeAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/v1/msfx/autorun/window?sourceApi=listupout");
        request.Headers.TryAddWithoutValidation(PacApiHeaders.MsfxRunLock, lockId.ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, persist.HoldCalls);
    }

    [Fact]
    public async Task Write_without_lock_returns_409()
    {
        var persist = new FakePersist();
        persist.SeedLock("listupout");
        await using var factory = new ApiFactory { AutoRunPersist = persist };
        using var client = factory.CreateClient();
        await AuthorizeAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/autorun/fail-interrupted")
        {
            Content = new StringContent(
                """{"sourceApi":"listupout","error":"interrupted"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, persist.HoldCalls);
    }

    private static async Task AuthorizeAsync(HttpClient client)
    {
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed class FakePersist : IMsfxAutoRunPersist
    {
        private Guid? _lockId;
        private string? _sourceApi;

        public int LockCalls { get; private set; }

        public int UnlockCalls { get; private set; }

        public int HoldCalls { get; private set; }

        public string? LastSourceApi { get; private set; }

        public Guid SeedLock(string sourceApi)
        {
            _sourceApi = sourceApi;
            _lockId = Guid.NewGuid();
            return _lockId.Value;
        }

        public Task<MsfxRunLockResult> TryAcquireLockAsync(string sourceApi, CancellationToken ct)
        {
            LockCalls++;
            LastSourceApi = sourceApi;
            _sourceApi = sourceApi;
            _lockId = Guid.NewGuid();
            return Task.FromResult(new MsfxRunLockResult(true, _lockId));
        }

        public Task ReleaseLockAsync(string sourceApi, Guid lockId, CancellationToken ct)
        {
            UnlockCalls++;
            LastSourceApi = sourceApi;
            if (_lockId == lockId && _sourceApi == sourceApi)
            {
                _lockId = null;
            }

            return Task.CompletedTask;
        }

        public Task<bool> RenewLockAsync(string sourceApi, Guid lockId, CancellationToken ct)
            => Task.FromResult(_lockId == lockId && _sourceApi == sourceApi);

        public bool TryHoldLock(Guid lockId, string? sourceApi)
        {
            HoldCalls++;
            if (_lockId != lockId)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sourceApi)
                && !string.Equals(_sourceApi, sourceApi, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public Task<int> FailInterruptedPullBatchesAsync(string sourceApi, string error, CancellationToken ct)
            => Task.FromResult(0);

        public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
            => Task.FromResult(new MsfxPullWindow(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));

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
            => Task.FromResult(1L);

        public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
            long billId,
            string billCode,
            IReadOnlyList<MsfxIngestDrugDto> drugs,
            CancellationToken ct)
            => throw new NotSupportedException();
    }
}
