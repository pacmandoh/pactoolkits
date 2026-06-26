using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

[CollectionDefinition("MsfxMappingBatchDialogViewModel", DisableParallelization = true)]
public sealed class MsfxMappingBatchDialogViewModelCollection;

[Collection("MsfxMappingBatchDialogViewModel")]
public sealed class MsfxMappingBatchDialogViewModelFilterTests
{
    [Fact]
    public async Task Filter_change_does_not_throw_when_load_groups_fails()
    {
        var sync = new ControllableMsfxSyncService
        {
            LoadGroupsHandler = (_, _) => throw new InvalidOperationException("boom")
        };

        var vm = CreateViewModel(sync);
        await vm.InitializeViewAsync().ConfigureAwait(true);

        vm.SelectedMapStatus = "PENDING";

        await Task.Delay(200).ConfigureAwait(true);

        Assert.Single(vm.Groups);
        Assert.Equal("seed-drug", vm.Groups[0].SourceDrugNameRaw);
    }

    [Fact]
    public async Task Stale_reload_result_is_discarded_when_newer_reload_starts()
    {
        var sync = new ControllableMsfxSyncService();
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        sync.LoadGroupsHandler = async (ct, callNum) =>
        {
            if (callNum == 1)
            {
                await firstGate.Task.WaitAsync(ct).ConfigureAwait(false);
                return [CreateRow("stale")];
            }

            return [CreateRow("fresh")];
        };

        var vm = CreateViewModel(sync);
        await vm.InitializeViewAsync().ConfigureAwait(true);

        vm.SelectedMapStatus = "PENDING";
        vm.SelectedCodeStatus = "NEW";

        await Task.Delay(200).ConfigureAwait(true);

        Assert.Equal("fresh", vm.Groups[0].SourceDrugNameRaw);

        firstGate.SetResult();
        await Task.Delay(200).ConfigureAwait(true);

        Assert.Equal("fresh", vm.Groups[0].SourceDrugNameRaw);
    }

    [Fact]
    public async Task Close_cancels_inflight_group_reload()
    {
        var sync = new ControllableMsfxSyncService();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        sync.LoadGroupsHandler = async (ct, _) =>
        {
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            return [CreateRow("late")];
        };

        var vm = CreateViewModel(sync);
        await vm.InitializeViewAsync().ConfigureAwait(true);

        vm.SelectedMapStatus = "PENDING";
        await Task.Delay(50).ConfigureAwait(true);
        vm.CloseCommand.Execute(null);

        await Task.Delay(200).ConfigureAwait(true);

        Assert.Single(vm.Groups);
        Assert.Equal("seed-drug", vm.Groups[0].SourceDrugNameRaw);
    }

    private static MsfxMappingBatchDialogViewModel CreateViewModel(ControllableMsfxSyncService sync)
    {
        var seed = CreateRow("seed-drug");
        return new MsfxMappingBatchDialogViewModel(
            new DialogManager(),
            new EmptyLookupCatalogService(),
            sync,
            new DbAccessGuard())
        {
            Model = new MsfxMappingBatchDialogModel(
                Groups: [seed],
                MapStatusFilter: "ALL",
                CodeStatusFilter: "ALL",
                SearchScope: "全部字段",
                Keyword: string.Empty)
        };
    }

    private static MsfxMappingBatchGroupRow CreateRow(string drugName)
        => new(
            SourceBillTimes: string.Empty,
            SourceBillCodes: string.Empty,
            SourceDrugNameRaw: drugName,
            SourceSpecRaw: "spec",
            SourceNameNorm: drugName,
            SourceSpecNorm: "spec",
            TotalCount: 1,
            PendingCount: 1,
            NeedReviewCount: 0,
            FailedCount: 0);

    private sealed class EmptyLookupCatalogService : ILookupCatalogService
    {
        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> ResolveCanonicalDrugIdAsync(string? input, CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<string?>(null);

        public Task<int?> GetQtyAsync(string? drugId, string? spec, CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult<int?>(null);

        public Task<bool> IsDeprecatedDrugIdAsync(string? drugId, CancellationToken ct, bool forceRefresh = false)
            => Task.FromResult(false);

        public void InvalidateDrugCatalog()
        {
        }
    }

    private sealed class ControllableMsfxSyncService : IMsfxSyncService
    {
        public Func<CancellationToken, int, Task<IReadOnlyList<MsfxMappingBatchGroupRow>>> LoadGroupsHandler { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<MsfxMappingBatchGroupRow>>([]);

        public int LoadGroupsCallCount { get; private set; }

        public Task<IReadOnlyList<MsfxMappingBatchGroupRow>> LoadMappingBatchGroupsAsync(
            string? mapStatus,
            string? codeStatus,
            string? searchScope,
            string? keyword,
            int limit,
            CancellationToken ct)
        {
            LoadGroupsCallCount++;
            return LoadGroupsHandler(ct, LoadGroupsCallCount);
        }

        public Task<MsfxMappingBatchPreview> PreviewMsfxMappingBatchAsync(
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
            => Task.FromResult(new MsfxMappingBatchPreview(0, 0, 0));

        public Task<MsfxPullWindow> LoadPullWindowAsync(string sourceApi, CancellationToken ct) => NotSupported<MsfxPullWindow>();
        public Task<MsfxPullBatchStartResult> StartMsfxPullBatchAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, CancellationToken ct) => NotSupported<MsfxPullBatchStartResult>();
        public Task CompleteMsfxPullBatchAsync(long batchId, string status, int successCount, int failCount, string? errMsg, CancellationToken ct) => NotSupported();
        public Task UpdateMsfxPullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct) => NotSupported();
        public Task AdvanceMsfxPullCursorAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, long batchId, string batchStatus, CancellationToken ct) => NotSupported();
        public Task<IReadOnlyList<MsfxBillRetryRow>> LoadDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct) => NotSupported<IReadOnlyList<MsfxBillRetryRow>>();
        public Task ScheduleBillRetryAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? lastError, CancellationToken ct) => NotSupported();
        public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct) => NotSupported();
        public Task WatchMsfxBillAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? fromEntName, string? billType, string? billTime, string? billUploadTime, string? lastSeenStatus, string? rawJson, CancellationToken ct) => NotSupported();
        public Task<IReadOnlyList<MsfxBillWatchRow>> LoadDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct) => NotSupported<IReadOnlyList<MsfxBillWatchRow>>();
        public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct) => NotSupported();
        public Task RescheduleBillWatchAsync(string sourceApi, string billCode, string? lastSeenStatus, string? lastError, CancellationToken ct) => NotSupported();
        public Task<long> SaveInboundBillAsync(long batchId, string billCode, string billType, string billTime, string billUploadTime, string fromRefUserId, string fromEntName, string toRefUserId, string toUserId, string toUserName, string status, string rawJson, CancellationToken ct) => NotSupported<long>();
        public Task<MsfxIngestDetailResult> IngestMsfxBillDetailAsync(long billId, string billCode, IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs, CancellationToken ct) => NotSupported<MsfxIngestDetailResult>();
        public Task<MsfxMapApplyResult> ApplyMsfxMappingAsync(int limit, CancellationToken ct) => NotSupported<MsfxMapApplyResult>();
        public Task<MsfxMappingStatusSnapshot> LoadMappingStatusSnapshotAsync(CancellationToken ct) => NotSupported<MsfxMappingStatusSnapshot>();
        public Task<MsfxBuildTaskResult> BuildMsfxInjectTasksAsync(int maxGroups, CancellationToken ct) => NotSupported<MsfxBuildTaskResult>();
        public Task<MsfxAutoBoardSnapshot> LoadMsfxDashboardAsync(CancellationToken ct) => NotSupported<MsfxAutoBoardSnapshot>();
        public Task<IReadOnlyList<MsfxPullBatchRow>> LoadRecentPullBatchesAsync(int limit, CancellationToken ct) => NotSupported<IReadOnlyList<MsfxPullBatchRow>>();
        public Task<MsfxMappingQueuePage> LoadMappingQueuePageAsync(int pageSize, string? mapStatus, string? codeStatus, string? searchScope, string? keyword, DateTimeOffset? cursorUpdatedAt, long? cursorId, bool newer, bool seekLastPage, CancellationToken ct) => NotSupported<MsfxMappingQueuePage>();
        public Task<IReadOnlyList<MsfxInjectTaskQueueRow>> LoadInjectTaskQueueAsync(int limit, CancellationToken ct) => NotSupported<IReadOnlyList<MsfxInjectTaskQueueRow>>();
        public Task<MsfxReopenInjectTaskResult> ReopenMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct) => NotSupported<MsfxReopenInjectTaskResult>();
        public Task<MsfxDiscardInjectTaskResult> DiscardMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct) => NotSupported<MsfxDiscardInjectTaskResult>();
        public Task<MsfxRemapInjectTaskResult> RemapMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct) => NotSupported<MsfxRemapInjectTaskResult>();
        public Task<MsfxMergeInjectTaskResult> MergeMsfxTasksAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct) => NotSupported<MsfxMergeInjectTaskResult>();
        public Task<MsfxSplitInjectTaskResult> SplitMsfxTaskAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct) => NotSupported<MsfxSplitInjectTaskResult>();
        public Task<IReadOnlyList<MsfxInjectTaskSplitUnitRow>> LoadMsfxTaskSplitUnitsAsync(long taskId, CancellationToken ct) => NotSupported<IReadOnlyList<MsfxInjectTaskSplitUnitRow>>();
        public Task<IReadOnlyList<MsfxInjectTaskSplitCodeRow>> LoadMsfxTaskSplitCodeRowsAsync(long taskId, CancellationToken ct) => NotSupported<IReadOnlyList<MsfxInjectTaskSplitCodeRow>>();
        public Task<MsfxSplitInjectTaskCustomResult> SplitMsfxTaskCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct) => NotSupported<MsfxSplitInjectTaskCustomResult>();
        public Task<MsfxMappingBatchApplyResult> ApplyMsfxMappingBatchAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct) => NotSupported<MsfxMappingBatchApplyResult>();

        private static Task<T> NotSupported<T>() => throw new NotSupportedException();
        private static Task NotSupported() => throw new NotSupportedException();
    }
}
