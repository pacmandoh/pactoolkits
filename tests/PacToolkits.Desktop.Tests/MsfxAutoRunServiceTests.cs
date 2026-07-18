using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services.Msfx;

namespace PacToolkits.Desktop.Tests;

public sealed class MsfxAutoRunServiceTests
{
    [Fact]
    public async Task RunAsync_completes_batch_and_advances_cursor_after_success()
    {
        var store = new FakeStore
        {
            IngestResult = new MsfxIngestDetailResult(1, 1, 2, 2),
            MapResult = new MsfxMapApplyResult(2, 2, 0),
            BuildResult = new MsfxBuildInject(1, 2)
        };
        var api = new FakeApi
        {
            List = _ => Task.FromResult(new MsfxListUpoutResult(
                OkCall(),
                1,
                [Upout("B-1", "2")])),
            Detail = request => Task.FromResult(DetailOk(request.BillCode))
        };
        var observer = new FakeObserver();

        var result = await new MsfxAutoRunService(api, store)
            .RunAsync(Request(), observer, CancellationToken.None);

        Assert.Equal(42, result.BatchId);
        Assert.Equal(1, result.ApiRows);
        Assert.Equal(1, result.InboundRows);
        Assert.Equal(2, result.Codes);
        Assert.Equal(1, result.CreatedTasks);
        Assert.Equal(
            [
                MsfxAutoRunData.PullAudit,
                MsfxAutoRunData.PullAudit,
                MsfxAutoRunData.PullAudit,
                MsfxAutoRunData.MappingQueue,
                MsfxAutoRunData.TaskQueue
            ],
            observer.ChangedData);
        Assert.Equal("SUCCESS", Assert.Single(store.FinishCalls).Status);
        Assert.Equal(1, store.AdvanceCount);
        Assert.Equal(1, store.LockReleaseCount);
    }

    [Fact]
    public async Task RunAsync_applies_large_mapping_backlog_in_bounded_batches()
    {
        var store = new FakeStore
        {
            MapResults = new Queue<MsfxMapApplyResult>(
            [
                new MsfxMapApplyResult(1000, 900, 100),
                new MsfxMapApplyResult(120, 100, 20)
            ])
        };

        var result = await new MsfxAutoRunService(new FakeApi(), store)
            .RunAsync(Request(), new FakeObserver(), CancellationToken.None);

        Assert.Equal(2, store.MappingApplyCount);
        Assert.Equal([1000, 1000], store.MappingLimits);
        Assert.Equal(1120, result.MapProcessed);
        Assert.Equal(1000, result.MapMatched);
        Assert.Equal(120, result.MapReview);
    }

    [Fact]
    public async Task RunAsync_finalizes_failed_batch_without_advancing_cursor_on_api_failure()
    {
        var store = new FakeStore();
        var api = new FakeApi
        {
            List = _ => Task.FromResult(new MsfxListUpoutResult(FailCall("list failed"), 0, []))
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MsfxAutoRunService(api, store)
                .RunAsync(Request(), new FakeObserver(), CancellationToken.None));

        Assert.Contains("list failed", error.Message, StringComparison.Ordinal);
        var finish = Assert.Single(store.FinishCalls);
        Assert.Equal("FAILED", finish.Status);
        Assert.Equal(1, finish.FailCount);
        Assert.Equal(0, store.AdvanceCount);
    }

    [Fact]
    public async Task RunAsync_processes_retry_success_and_defers_unready_watch()
    {
        var store = new FakeStore
        {
            Retries =
            [
                new MsfxBillRetryRow("R-1", "FROM", "TO", 1, DateTimeOffset.UtcNow, "previous")
            ],
            Watches =
            [
                new MsfxBillWatchRow(
                    "W-1", "FROM", "TO", "ORG", "TYPE", "TIME", "UPLOAD", "1", "{}", 1,
                    DateTimeOffset.UtcNow)
            ],
            IngestResult = new MsfxIngestDetailResult(1, 1, 3, 1)
        };
        var api = new FakeApi
        {
            List = _ => Task.FromResult(new MsfxListUpoutResult(OkCall(), 0, [])),
            Detail = request => Task.FromResult(request.BillCode == "R-1"
                ? DetailOk(request.BillCode)
                : new MsfxListUpoutDetailResult(FailCall("not ready"), request.BillCode, [], [], string.Empty))
        };
        var observer = new FakeObserver();

        var result = await new MsfxAutoRunService(api, store)
            .RunAsync(Request(), observer, CancellationToken.None);

        Assert.Equal(1, result.RetrySucceeded);
        Assert.Equal(1, result.WatchDeferred);
        Assert.Equal(1, store.RetrySucceededCount);
        Assert.Equal(1, store.WatchRescheduledCount);
        Assert.Contains(
            observer.Updates,
            update => update.Message?.Contains(
                "药品处理 1/新增 1，码处理 3/新增 1",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task RunAsync_finalizes_failed_batch_when_cancelled_after_batch_start()
    {
        var store = new FakeStore();
        var api = new FakeApi
        {
            List = _ => Task.FromException<MsfxListUpoutResult>(new OperationCanceledException())
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new MsfxAutoRunService(api, store)
                .RunAsync(Request(), new FakeObserver(), CancellationToken.None));

        Assert.Equal("FAILED", Assert.Single(store.FinishCalls).Status);
        Assert.Equal(0, store.AdvanceCount);
    }

    [Fact]
    public async Task RunAsync_skips_mapping_and_task_build_while_manual_write_is_active()
    {
        var store = new FakeStore();
        var observer = new FakeObserver { IsManualWriteActive = true };

        var result = await new MsfxAutoRunService(new FakeApi(), store)
            .RunAsync(Request(), observer, CancellationToken.None);

        Assert.Equal(0, store.MappingApplyCount);
        Assert.Equal(0, store.TaskBuildCount);
        Assert.DoesNotContain(MsfxAutoRunData.MappingQueue, observer.ChangedData);
        Assert.DoesNotContain(MsfxAutoRunData.TaskQueue, observer.ChangedData);
        Assert.Equal("SUCCESS", Assert.Single(store.FinishCalls).Status);
        Assert.Equal(1, store.AdvanceCount);
        Assert.Equal(0, result.CreatedTasks);
    }

    [Fact]
    public async Task RunAsync_skips_mapping_call_when_no_pending_rows_exist()
    {
        var store = new FakeStore { MappingPendingCount = 0 };
        var observer = new FakeObserver();

        var result = await new MsfxAutoRunService(new FakeApi(), store)
            .RunAsync(Request(), observer, CancellationToken.None);

        Assert.Equal(0, store.MappingApplyCount);
        Assert.Equal(1, store.TaskBuildCount);
        Assert.Equal(0, result.MapProcessed);
        Assert.Contains(
            observer.Updates,
            update => string.Equals(update.Message, "无待映射数据，跳过自动映射", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_keeps_current_bill_progress_within_its_stage_total()
    {
        var store = new FakeStore
        {
            Retries =
            [
                new MsfxBillRetryRow("R-1", "FROM", "TO", 1, DateTimeOffset.UtcNow, "previous")
            ]
        };
        var api = new FakeApi
        {
            List = _ => Task.FromResult(new MsfxListUpoutResult(
                OkCall(),
                2,
                [Upout("B-1", "2"), Upout("B-2", "2")]))
        };
        var observer = new FakeObserver();

        await new MsfxAutoRunService(api, store)
            .RunAsync(Request(), observer, CancellationToken.None);

        Assert.Equal(
            ["解析单据 1/2：B-1", "解析单据 2/2：B-2"],
            observer.Updates
                .Select(update => update.Status)
                .Where(status => status?.StartsWith("解析单据", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task RunAsync_uses_fixed_pull_stage_progress_across_pages()
    {
        var firstPage = Enumerable.Range(1, 50)
            .Select(index => Upout($"B-{index}", "2"))
            .ToArray();
        var api = new FakeApi
        {
            List = request => Task.FromResult(request.Page == 1
                ? new MsfxListUpoutResult(OkCall(), 51, firstPage)
                : new MsfxListUpoutResult(OkCall(), 51, [Upout("B-51", "2")]))
        };
        var observer = new FakeObserver();

        await new MsfxAutoRunService(api, new FakeStore())
            .RunAsync(Request(), observer, CancellationToken.None);

        var pullProgress = observer.Updates
            .Where(update => update.Status?.StartsWith("解析单据", StringComparison.Ordinal) == true)
            .Select(update => update.Progress!.Value)
            .ToArray();
        Assert.Equal(51, pullProgress.Length);
        Assert.Equal(pullProgress.Order(), pullProgress);
        Assert.All(pullProgress, value => Assert.InRange(value, 15, 65));
        Assert.True(pullProgress[0] < 25);
        Assert.True(pullProgress[^1] > 60);
    }

    [Fact]
    public async Task RunAsync_queues_detail_timeout_without_failing_the_batch()
    {
        var store = new FakeStore();
        var api = new FakeApi
        {
            List = _ => Task.FromResult(new MsfxListUpoutResult(
                OkCall(),
                1,
                [Upout("B-TIMEOUT", "2")])),
            Detail = _ => Task.FromException<MsfxListUpoutDetailResult>(new OperationCanceledException())
        };

        var result = await new MsfxAutoRunService(api, store)
            .RunAsync(Request(), new FakeObserver(), CancellationToken.None);

        Assert.Equal(1, result.RetryQueued);
        Assert.Equal(1, store.RetryQueuedCount);
        Assert.Equal("SUCCESS", Assert.Single(store.FinishCalls).Status);
        Assert.Equal(1, store.AdvanceCount);
    }

    [Fact]
    public async Task RecoverInterruptedAsync_fails_batches_left_running_by_a_previous_process()
    {
        var store = new FakeStore { InterruptedBatchCount = 2 };
        var service = new MsfxAutoRunService(new FakeApi(), store);

        var recovered = await service.RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal(2, recovered);
        Assert.Equal(1, store.RecoveryCount);
    }

    [Fact]
    public async Task RecoverInterruptedAsync_leaves_active_batch_untouched_when_lock_is_held()
    {
        var store = new FakeStore
        {
            InterruptedBatchCount = 2,
            LockAvailable = false
        };

        var recovered = await new MsfxAutoRunService(new FakeApi(), store)
            .RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal(0, recovered);
        Assert.Equal(0, store.RecoveryCount);
    }

    [Fact]
    public async Task RunAsync_stops_before_creating_batch_when_another_process_holds_lock()
    {
        var store = new FakeStore { LockAvailable = false };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MsfxAutoRunService(new FakeApi(), store)
                .RunAsync(Request(), new FakeObserver(), CancellationToken.None));

        Assert.Contains("正在运行", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.BatchStartCount);
        Assert.Equal(0, store.RecoveryCount);
    }

    [Fact]
    public async Task RunAsync_reconciles_page_drift_until_no_unseen_bill_remains()
    {
        var page2Calls = 0;
        var firstPage = Enumerable.Range(1, 50)
            .Select(index => Upout($"B-{index}", "2"))
            .ToArray();
        var api = new FakeApi
        {
            List = request =>
            {
                if (request.Page == 1)
                {
                    return Task.FromResult(new MsfxListUpoutResult(OkCall(), 51, firstPage));
                }

                page2Calls++;
                var item = page2Calls == 1
                    ? Upout("B-50", "2")
                    : Upout("B-51", "2");
                return Task.FromResult(new MsfxListUpoutResult(OkCall(), 51, [item]));
            }
        };
        var store = new FakeStore();
        var observer = new FakeObserver();

        var result = await new MsfxAutoRunService(api, store)
            .RunAsync(Request(), observer, CancellationToken.None);

        Assert.Equal(51, result.Bills);
        Assert.Equal(51, store.IngestedBills.Count);
        Assert.Contains("B-51", store.IngestedBills);
        Assert.Equal(3, page2Calls);
        var progress = observer.Updates
            .Where(update => update.Progress.HasValue)
            .Select(update => update.Progress!.Value)
            .ToArray();
        Assert.Equal(progress.Order(), progress);
    }

    private static MsfxAutoRunRequest Request()
        => new(new MsfxApiOptions { RefEntId = "REF", TimeoutSeconds = 20 });

    private static MsfxApiCallResult OkCall()
        => new(true, 200, "ok", string.Empty, string.Empty, "REQ", "{}", string.Empty);

    private static MsfxApiCallResult FailCall(string summary)
        => new(false, 200, summary, "ERR", summary, "REQ", "{}", string.Empty);

    private static MsfxListUpoutDetailResult DetailOk(string billCode)
        => new(
            OkCall(),
            billCode,
            [
                new MsfxDrugDetailItem(
                    "Drug",
                    "Package",
                    "Spec",
                    "Batch",
                    [new MsfxTraceCodeItem("CODE", "1")])
            ],
            ["CODE"],
            string.Empty);

    private static MsfxListUpoutItem Upout(string billCode, string status)
        => new(
            BillCode: billCode,
            BillType: "TYPE",
            BillTime: "2026-07-17",
            BillUploadTime: "2026-07-17",
            PhysicName: "Drug",
            PkgSpec: "Package",
            PrepnSpec: "Spec",
            PrepnCount: 1,
            CodeCount: 1,
            ProduceBatchNo: "Batch",
            ExpireDate: "2027-07-17",
            FromEntName: "ORG",
            ProduceEntName: "FACTORY",
            FromRefUserId: "FROM",
            ToRefUserId: "TO",
            ConfirmStatus: string.Empty,
            DrugTag: string.Empty,
            IsCollectDrugBill: string.Empty,
            IsSpecialDrugBill: string.Empty,
            IsBloodProductBill: string.Empty,
            IsBiologicalProductBill: string.Empty,
            IsBotulinumBill: string.Empty,
            VerifyStatus: string.Empty,
            RegulatedFlag: string.Empty,
            LogisticsStatus: string.Empty,
            Status: status);

    private sealed class FakeObserver : IMsfxAutoRunObserver
    {
        public bool IsManualWriteActive { get; set; }
        public List<MsfxAutoRunUpdate> Updates { get; } = [];
        public List<MsfxAutoRunData> ChangedData { get; } = [];

        public void Report(MsfxAutoRunUpdate update) => Updates.Add(update);

        public Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct)
        {
            ChangedData.Add(data);
            return Task.CompletedTask;
        }

    }

    private sealed class FakeApi : IMsfxApiClient
    {
        public Func<MsfxListUpoutRequest, Task<MsfxListUpoutResult>> List { get; init; } =
            _ => Task.FromResult(new MsfxListUpoutResult(OkCall(), 0, []));

        public Func<MsfxListUpoutDetailRequest, Task<MsfxListUpoutDetailResult>> Detail { get; init; } =
            request => Task.FromResult(DetailOk(request.BillCode));

        public Task<MsfxApiCallResult> ExecuteRawAsync(
            MsfxApiOptions options,
            string method,
            IReadOnlyDictionary<string, string?> bizParams,
            CancellationToken ct)
            => Task.FromResult(OkCall());

        public Task<MsfxListUpoutResult> GetYljgListUpoutAsync(
            MsfxApiOptions options,
            MsfxListUpoutRequest request,
            CancellationToken ct)
            => List(request);

        public Task<MsfxListUpoutDetailResult> GetYljgListUpoutDetailAsync(
            MsfxApiOptions options,
            MsfxListUpoutDetailRequest request,
            CancellationToken ct)
            => Detail(request);
    }

    private sealed class FakeStore : IMsfxAutoRunStore
    {
        public IReadOnlyList<MsfxBillRetryRow> Retries { get; init; } = [];
        public IReadOnlyList<MsfxBillWatchRow> Watches { get; init; } = [];
        public MsfxIngestDetailResult IngestResult { get; init; } = new(0, 0, 0, 0);
        public MsfxMapApplyResult MapResult { get; init; } = new(0, 0, 0);
        public int MappingPendingCount { get; init; } = 1;
        public Queue<MsfxMapApplyResult> MapResults { get; init; } = new();
        public MsfxBuildInject BuildResult { get; init; } = new(0, 0);
        public int InterruptedBatchCount { get; init; }
        public bool LockAvailable { get; init; } = true;
        public List<FinishCall> FinishCalls { get; } = [];
        public List<string> IngestedBills { get; } = [];
        public int BatchStartCount { get; private set; }
        public int AdvanceCount { get; private set; }
        public int RetrySucceededCount { get; private set; }
        public int RetryQueuedCount { get; private set; }
        public int WatchRescheduledCount { get; private set; }
        public int MappingApplyCount { get; private set; }
        public List<int> MappingLimits { get; } = [];
        public int TaskBuildCount { get; private set; }
        public int RecoveryCount { get; private set; }
        public int LockReleaseCount { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireRunLockAsync(
            string sourceApi,
            CancellationToken ct)
            => Task.FromResult<IAsyncDisposable?>(
                LockAvailable ? new FakeLock(() => LockReleaseCount++) : null);

        public Task<int> FailInterruptedPullBatchesAsync(
            string sourceApi,
            string error,
            CancellationToken ct)
        {
            RecoveryCount++;
            return Task.FromResult(InterruptedBatchCount);
        }

        public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
            => Task.FromResult(new MsfxPullWindow(
                new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero)));

        public Task<MsfxPullBatchStartResult> StartPullBatchAsync(
            string sourceApi,
            DateTimeOffset beginAt,
            DateTimeOffset endAt,
            CancellationToken ct)
        {
            BatchStartCount++;
            return Task.FromResult(new MsfxPullBatchStartResult(42));
        }

        public Task FinishPullBatchAsync(
            long batchId,
            string status,
            int successCount,
            int failCount,
            string? errMsg,
            CancellationToken ct)
        {
            FinishCalls.Add(new FinishCall(status, successCount, failCount, errMsg));
            return Task.CompletedTask;
        }

        public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
            => Task.CompletedTask;

        public Task AdvancePullCursorAsync(
            string sourceApi,
            DateTimeOffset beginAt,
            DateTimeOffset endAt,
            long batchId,
            string batchStatus,
            CancellationToken ct)
        {
            AdvanceCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
            string sourceApi,
            int limit,
            CancellationToken ct)
            => Task.FromResult(Retries);

        public Task UpsertBillRetryAsync(
            string sourceApi,
            string billCode,
            string? fromRefUserId,
            string? toRefUserId,
            string? lastError,
            CancellationToken ct)
        {
            RetryQueuedCount++;
            return Task.CompletedTask;
        }

        public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
        {
            RetrySucceededCount++;
            return Task.CompletedTask;
        }

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
            => Task.FromResult(Watches);

        public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
            => Task.CompletedTask;

        public Task RescheduleBillWatchAsync(
            string sourceApi,
            string billCode,
            string? lastSeenStatus,
            string? lastError,
            CancellationToken ct)
        {
            WatchRescheduledCount++;
            return Task.CompletedTask;
        }

        public Task<long> UpsertInboundBillAsync(
            long batchId,
            string billCode,
            string billType,
            string billTime,
            string billUploadTime,
            string fromRefUserId,
            string fromEntName,
            string toRefUserId,
            string toUserId,
            string toUserName,
            string status,
            string rawJson,
            CancellationToken ct)
            => Task.FromResult(100L);

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
        {
            IngestedBills.Add(billCode);
            return Task.FromResult(IngestResult);
        }

        public Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
            => Task.FromResult(new MsfxMappingStatusSnapshot(MappingPendingCount, 0, 0, 0, MappingPendingCount));

        public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
        {
            MappingApplyCount++;
            MappingLimits.Add(limit);
            return Task.FromResult(MapResults.TryDequeue(out var result) ? result : MapResult);
        }

        public Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct)
        {
            TaskBuildCount++;
            return Task.FromResult(BuildResult);
        }
    }

    private sealed class FakeLock(Action released) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            released();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record FinishCall(string Status, int SuccessCount, int FailCount, string? Error);
}
