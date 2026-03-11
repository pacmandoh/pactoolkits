using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pactoolkits_ui.Contracts;

namespace pactoolkits_ui.Repositories;

public interface IDashboardRepo
{
    Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct);

    Task<IReadOnlyList<(string Client, long Value)>> GetClientsAsync(DashboardQuery q, CancellationToken ct);

    Task<DashboardKpiDto> GetKpisAsync(
        DashboardQuery q,
        CancellationToken ct);

    Task<PagedResult<TrendRowDto>> GetTrendPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TraceTxnDto>> GetRecentTxnsPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<AbnormalRowDto>> GetAbnormalQueuePageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TraceEntryLogDto>> GetEntryLogsPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct);
}

public interface IDrugIndexRepo
{
    Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct);

    Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct);

    Task<bool> IsDrugDeprecatedAsync(string drugId, CancellationToken ct);

    Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct);

    Task DeleteAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct);

    Task<DrugKeyFixApplyResultDto> ApplyKeyFixAsync(
        DrugIndexDto source,
        DrugIndexDto target,
        string reason,
        string operatorName,
        string sourceTag,
        CancellationToken ct);
}

public interface IInventoryOverviewRepo
{
    Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task UpdateStockCellAsync(
        string traceCode,
        string columnHeader,
        string? rawValue,
        CancellationToken ct);

    Task<int> DeleteStockByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        CancellationToken ct);

    Task<StockReassignPreviewDto> PreviewStockReassignByTraceCodeAsync(
        string traceCode,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignStockByTraceCodeAsync(
        string traceCode,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct);

    Task<StockReassignPreviewDto> PreviewStockReassignByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignStockByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct);
}

public sealed record ScanCodeInsertResult(int RequestedCount, int InsertedCount, int SkippedCount);

public interface IScanCodeRepo
{
    Task<ScanCodeInsertResult> InsertTraceCodesAsync(
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken ct);
}

public sealed record MsfxPullWindow(DateTimeOffset BeginAt, DateTimeOffset EndAt);
public sealed record MsfxPullBatchStartResult(long BatchId);
public sealed record MsfxBillRetryRow(
    string BillCode,
    string? FromRefUserId,
    string? ToRefUserId,
    int RetryCount,
    DateTimeOffset NextRetryAt,
    string? LastError);
public sealed record MsfxMapApplyResult(int ProcessedCount, int MappedCount, int ReviewCount);
public sealed record MsfxMappingStatusSnapshot(
    int PendingCount,
    int MappedCount,
    int NeedReviewCount,
    int FailedCount,
    int TotalCount);
public sealed record MsfxBuildTaskResult(int CreatedTasks, int TaskedCodes);
public sealed record MsfxIngestDetailResult(int InsertedItems, int InsertedCodes, int InsertedStaging);
public sealed record MsfxMappingBacklogDiagnostic(
    int PendingCount,
    int PendingWithDrugRawCount,
    int PendingWithSpecRawCount,
    int PendingWithRelationCount,
    int PendingWithNameNormCount,
    int PendingWithSpecNormCount);
public sealed record MsfxPullBatchRow(
    long BatchId,
    string SourceApi,
    DateTime? BeginDate,
    DateTime? EndDate,
    string Status,
    int SuccessCount,
    int FailCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrMsg);
public sealed record MsfxMappingQueueRow(
    long StagingId,
    string LeafCode,
    string MapStatus,
    string CodeStatus,
    string? MapReasonCode,
    string? MapReasonDetail,
    string? SourceBillCode,
    string? SourceDrugNameRaw,
    string? SourceSpecRaw,
    string? SourceNameNorm,
    string? SourceSpecNorm,
    string? SourceCodeLevel1,
    string? SourceCodeLevel2,
    string? SourceCodeLevel3,
    string? SourceCodeLevel4,
    string? SourceCodeLevel5,
    string? MappedDrugId,
    string? MappedSpec,
    DateTimeOffset UpdatedAt);
public sealed record MsfxMappingQueuePage(
    IReadOnlyList<MsfxMappingQueueRow> Rows,
    int TotalCount,
    bool HasNewer,
    bool HasOlder);
public sealed record MsfxMappingBatchPreview(
    int CandidateCount,
    int EligibleCount,
    int BlockedCount);
public sealed record MsfxMappingBatchApplyResult(
    int AffectedCount);
public sealed record MsfxMappingBatchGroupRow(
    string SourceDrugNameRaw,
    string SourceSpecRaw,
    string SourceNameNorm,
    string SourceSpecNorm,
    int TotalCount,
    int PendingCount,
    int NeedReviewCount,
    int FailedCount);
public sealed record MsfxInjectTaskQueueRow(
    long TaskId,
    string Status,
    string? SourceBillCode,
    string MappedDrugId,
    string MappedSpec,
    int TotalCodes,
    int SuccessCodes,
    int FailedCodes,
    int RetryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PickedAt,
    DateTimeOffset? FinishedAt,
    string? ErrMsg);
public sealed record MsfxAutoBoardSnapshot(
    long LastBatchId,
    string LastBatchStatus,
    DateTimeOffset? LastBatchStartedAt,
    DateTimeOffset? LastBatchFinishedAt,
    int LastBatchSuccessCount,
    int LastBatchFailCount,
    int StagingNewCount,
    int StagingTaskedCount,
    int StagingInjectedCount,
    int StagingVerifiedCount,
    int StagingPooledCount,
    int StagingFailedCount,
    int StagingDuplicateCount,
    int MapPendingCount,
    int MapMappedCount,
    int MapNeedReviewCount,
    int MapFailedCount,
    int TaskNewCount,
    int TaskRunningCount,
    int TaskSuccessCount,
    int TaskPartialCount,
    int TaskFailedCount,
    int TaskCancelledCount);

public interface IMsfxSyncRepo
{
    Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct);

    Task<MsfxPullBatchStartResult> StartPullBatchAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        CancellationToken ct);

    Task FinishPullBatchAsync(
        long batchId,
        string status,
        int successCount,
        int failCount,
        string? errMsg,
        CancellationToken ct);

    Task UpdatePullBatchRequestIdAsync(
        long batchId,
        string? requestId,
        CancellationToken ct);

    Task AdvancePullCursorAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        long batchId,
        string batchStatus,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct);

    Task UpsertBillRetryAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? lastError,
        CancellationToken ct);

    Task MarkBillRetrySucceededAsync(
        string sourceApi,
        string billCode,
        CancellationToken ct);

    Task<long> UpsertInboundBillAsync(
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
        CancellationToken ct);

    Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs,
        CancellationToken ct);

    Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct);
    Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct);
    Task<MsfxMappingBacklogDiagnostic> GetMappingBacklogDiagnosticAsync(CancellationToken ct);

    Task<MsfxBuildTaskResult> BuildInjectTasksAsync(int maxGroups, CancellationToken ct);

    Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct);

    Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct);

    Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectTaskQueueRow>> GetInjectTaskQueueAsync(int limit, CancellationToken ct);

    Task<bool> ApplyManualMappingAsync(long stagingId, string drugId, string spec, CancellationToken ct);

    Task MarkNeedReviewAsync(long stagingId, CancellationToken ct);

    Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int limit,
        CancellationToken ct);

    Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
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
        CancellationToken ct);

    Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
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
        CancellationToken ct);
}
