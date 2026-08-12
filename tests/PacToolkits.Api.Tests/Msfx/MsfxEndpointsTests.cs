using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class MsfxEndpointsTests
{
    [Fact]
    public async Task GetBoard_returns_snapshot()
    {
        var sync = new FakeSync
        {
            Board = CreateBoard(lastBatchId: 1, status: "SUCCESS"),
        };
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/msfx/board", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sync.BoardCalls);
    }

    [Fact]
    public async Task ApplyMapping_uses_sync()
    {
        var sync = new FakeSync();
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/mapping/apply")
        {
            Content = new StringContent("""{"limit":10}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sync.ApplyMappingCalls);
        Assert.Equal(10, sync.LastApplyLimit);
    }

    [Fact]
    public async Task ApplyMapping_empty_body_uses_default_limit()
    {
        var sync = new FakeSync();
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/mapping/apply");
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sync.ApplyMappingCalls);
        Assert.Equal(200, sync.LastApplyLimit);
    }

    [Fact]
    public async Task ApplyMapping_rejects_invalid_json()
    {
        var sync = new FakeSync();
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/mapping/apply")
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, sync.ApplyMappingCalls);
    }

    [Fact]
    public async Task BuildInject_rejects_invalid_json()
    {
        var sync = new FakeSync();
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/inject/build")
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, sync.BuildInjectCalls);
    }

    [Fact]
    public async Task ReopenInject_same_command_id_different_path_id_is_payload_mismatch()
    {
        var sync = new FakeSync();
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var commandId = Guid.NewGuid();
        var body = """{"operatorName":"op","reason":"r"}""";

        using var first = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/inject/11/reopen")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        first.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, commandId.ToString("D"));
        using var firstResponse = await client.SendAsync(first, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(1, sync.ReopenCalls);
        Assert.Equal(11, sync.LastReopenTaskId);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/inject/22/reopen")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        second.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, commandId.ToString("D"));
        using var secondResponse = await client.SendAsync(second, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var problem = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("payload_mismatch", problem, StringComparison.Ordinal);
        Assert.Equal(1, sync.ReopenCalls);
    }

    [Fact]
    public async Task AdvanceCursor_uses_sync()
    {
        var sync = new FakeSync();
        await using var factory = new ApiFactory { Sync = sync };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/msfx/cursor/advance")
        {
            Content = new StringContent(
                """{"sourceApi":"listupout","target":"2026-01-01T00:00:00+08:00"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sync.AdvanceCalls);
    }

    private static MsfxAutoBoardSnapshot CreateBoard(long lastBatchId, string status)
        => new(
            LastBatchId: lastBatchId,
            LastBatchStatus: status,
            LastBatchStartedAt: null,
            LastBatchFinishedAt: null,
            LastBatchSuccessCount: 0,
            LastBatchFailCount: 0,
            StagingNewCount: 0,
            StagingTaskedCount: 0,
            StagingInjectedCount: 0,
            StagingVerifiedCount: 0,
            StagingPooledCount: 0,
            StagingFailedCount: 0,
            StagingDuplicateCount: 0,
            MapPendingCount: 0,
            MapMappedCount: 0,
            MapNeedReviewCount: 0,
            MapFailedCount: 0,
            TaskNewCount: 0,
            TaskRunningCount: 0,
            TaskSuccessCount: 0,
            TaskFailedCount: 0,
            TaskCancelledCount: 0,
            TaskDiscardedCount: 0);

    private sealed class FakeSync : ISyncService
    {
        public MsfxAutoBoardSnapshot Board { get; init; } = CreateBoard(0, string.Empty);

        public int BoardCalls { get; private set; }
        public int ApplyMappingCalls { get; private set; }
        public int AdvanceCalls { get; private set; }
        public int BuildInjectCalls { get; private set; }
        public int ReopenCalls { get; private set; }
        public int LastApplyLimit { get; private set; }
        public long LastReopenTaskId { get; private set; }

        public Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct)
            => Task.FromResult(new MsfxPullCursorState(null, null, null, null));

        public Task<MsfxPullCursorState> AdvancePullCursorToAsync(
            string sourceApi,
            DateTimeOffset target,
            CancellationToken ct)
        {
            AdvanceCalls++;
            return GetPullCursorAsync(sourceApi, ct);
        }

        public Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct)
        {
            BuildInjectCalls++;
            return Task.FromResult(new MsfxBuildInject(0, 0));
        }

        public Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct)
        {
            BoardCalls++;
            return Task.FromResult(Board);
        }

        public Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxPullBatchRow>>([]);

        public Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
            int pageSize,
            IReadOnlyCollection<string>? mapStatuses,
            string? codeStatus,
            string? searchScope,
            string? keyword,
            DateTimeOffset? cursorUpdatedAt,
            long? cursorId,
            bool newer,
            bool seekLastPage,
            CancellationToken ct)
            => Task.FromResult(new MsfxMappingQueuePage([], TotalCount: 0, HasNewer: false, HasOlder: false));

        public Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxInjectQueueRow>>([]);

        public Task<MsfxInjectReopen> ReopenInjectAsync(
            long taskId,
            string? operatorName,
            string? reason,
            CancellationToken ct)
        {
            ReopenCalls++;
            LastReopenTaskId = taskId;
            return Task.FromResult(new MsfxInjectReopen(taskId, Status: "NEW", TotalCodes: 0));
        }

        public Task<MsfxInjectDiscard> DiscardInjectAsync(
            long taskId,
            string? operatorName,
            string? reason,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MsfxInjectRemap> RemapInjectAsync(
            long taskId,
            string? operatorName,
            string? reason,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MsfxInjectMerge> MergeInjectsAsync(
            IReadOnlyList<long> taskIds,
            string? operatorName,
            string? reason,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MsfxInjectSplit> SplitInjectAsync(
            long taskId,
            string splitMode,
            string? operatorName,
            string? reason,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(
            long taskId,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxInjectSplitUnitRow>>([]);

        public Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(
            long taskId,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxInjectSplitCodeRow>>([]);

        public Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(
            long taskId,
            IReadOnlyList<string> groupKeys,
            IReadOnlyList<int> bucketIndexes,
            string? operatorName,
            string? reason,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
        {
            ApplyMappingCalls++;
            LastApplyLimit = limit;
            return Task.FromResult(new MsfxMapApplyResult(0, 0, 0));
        }

        public Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
            string? mapStatus,
            string? codeStatus,
            string? searchScope,
            string? keyword,
            int limit,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MsfxMappingBatchGroupRow>>([]);

        public Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
            string? mapStatus,
            string? codeStatus,
            string? searchScope,
            string? keyword,
            string? groupSourceDrugNameRaw,
            string? groupSourceSpecRaw,
            string? groupSourceNameNorm,
            string? groupSourceSpecNorm,
            string action,
            string? drugId,
            string? spec,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
            string? mapStatus,
            string? codeStatus,
            string? searchScope,
            string? keyword,
            string? groupSourceDrugNameRaw,
            string? groupSourceSpecRaw,
            string? groupSourceNameNorm,
            string? groupSourceSpecNorm,
            string action,
            string? drugId,
            string? spec,
            CancellationToken ct)
            => throw new NotSupportedException();
    }
}
