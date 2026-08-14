using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>Injector 用例：预留/提交/回滚与仓库任务回写</summary>
public interface IInjectorService
{
    Task<InjectorReserveResult> ReserveAsync(InjectorReserveRequest request, CancellationToken ct);

    Task<InjectorTxnResult> CommitAsync(string txnId, CancellationToken ct);

    Task<InjectorTxnResult> RollbackAsync(string txnId, CancellationToken ct);

    Task<InjectorCleanupResult> CleanupPendingAsync(int timeoutMinutes, int maxBatch, CancellationToken ct);

    Task<InjectorClaimResult> ClaimByTargetAsync(
        string clientId,
        string drugId,
        string spec,
        CancellationToken ct);

    Task<bool> HasWarehouseSuccessAsync(
        string warehouseBillNo,
        string drugId,
        string spec,
        string? rowFingerprint,
        CancellationToken ct);

    Task<IReadOnlyList<InjectorPendingCode>> GetPendingCodesAsync(long taskId, CancellationToken ct);

    Task UpdateTaskCodesAsync(
        long taskId,
        IReadOnlyList<string> leafCodes,
        string status,
        string? verifyResult,
        string? errMsg,
        CancellationToken ct);

    Task UpdateStagingStatusAsync(
        IReadOnlyList<long> stagingIds,
        string codeStatus,
        string? errMsg,
        CancellationToken ct);

    Task InsertEventAsync(
        long taskId,
        string stage,
        string level,
        string message,
        string? leafCode,
        CancellationToken ct);

    Task<InjectorFinalizeResult> FinalizeAsync(
        long taskId,
        string? errMsg,
        string? warehouseBillNo,
        string? rowFingerprint,
        CancellationToken ct);
}
