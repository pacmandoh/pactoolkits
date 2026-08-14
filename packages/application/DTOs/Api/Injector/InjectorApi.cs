namespace PacToolkits.Application.DTOs;

/// <summary>Injector 预留与仓库任务 PacAPI 体</summary>
public sealed record InjectorReserveRequest(
    string TxnId,
    string ClientId,
    string DrugId,
    string Spec,
    int WholeN,
    int RemNeed);

public sealed record InjectorReserveItem(long PoolId, int Take, string Code, int Seq);

public sealed record InjectorReserveResult(
    bool Ok,
    string? Reason,
    string? Message,
    IReadOnlyList<InjectorReserveItem> Items,
    int SumTake,
    int WholeN,
    int RemNeed);

public sealed record InjectorTxnIdRequest(string TxnId);

public sealed record InjectorTxnResult(bool Ok, string? Message, int RestoredRows);

public sealed record InjectorCleanupRequest(int TimeoutMinutes, int MaxBatch);

public sealed record InjectorCleanupResult(bool Ok, int Cleaned);

public sealed record InjectorClaimRequest(string ClientId, string DrugId, string Spec);

public sealed record InjectorClaimedTask(
    long TaskId,
    long BillId,
    string SourceBillCode,
    string MappedDrugId,
    string MappedSpec,
    int TotalCodes);

public sealed record InjectorClaimResult(IReadOnlyList<InjectorClaimedTask> Tasks);

public sealed record InjectorPendingCode(
    int Seq,
    string LeafCode,
    long StagingId,
    string L1,
    string L2,
    string L3,
    string L4,
    string L5);

public sealed record InjectorCodesStatusRequest(
    IReadOnlyList<string> LeafCodes,
    string Status,
    string? VerifyResult,
    string? ErrMsg);

public sealed record InjectorStagingStatusRequest(
    IReadOnlyList<long> StagingIds,
    string CodeStatus,
    string? ErrMsg);

public sealed record InjectorEventRequest(
    string Stage,
    string Level,
    string Message,
    string? LeafCode);

public sealed record InjectorFinalizeRequest(
    string? ErrMsg,
    string? WarehouseBillNo,
    string? RowFingerprint);

public sealed record InjectorFinalizeRow(
    long TaskId,
    string TaskStatus,
    int SuccessCodes,
    int FailedCodes,
    int TotalCodes);

public sealed record InjectorFinalizeResult(IReadOnlyList<InjectorFinalizeRow> Rows);

public sealed record InjectorOkResult(bool Ok, string? Message);

public sealed record InjectorExistsResult(bool Exists);
