using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

/// <summary>Injector 预留与仓库任务：转仓储，清理 PENDING 在服务端循环回滚</summary>
public sealed class InjectorService : IInjectorService
{
    private readonly IInjectorRepo _repo;

    public InjectorService(IInjectorRepo repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public Task<InjectorReserveResult> ReserveAsync(InjectorReserveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireText(request.TxnId, nameof(request.TxnId));
        RequireText(request.ClientId, nameof(request.ClientId));
        RequireText(request.DrugId, nameof(request.DrugId));
        RequireText(request.Spec, nameof(request.Spec));
        if (request.WholeN < 0 || request.RemNeed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "wholeN and remNeed must be >= 0");
        }

        if (request.WholeN == 0 && request.RemNeed == 0)
        {
            return Task.FromResult(new InjectorReserveResult(
                Ok: true,
                Reason: "SKIP",
                Message: null,
                Items: [],
                SumTake: 0,
                WholeN: 0,
                RemNeed: 0));
        }

        return _repo.ReserveAsync(request, ct);
    }

    public Task<InjectorTxnResult> CommitAsync(string txnId, CancellationToken ct)
    {
        RequireText(txnId, nameof(txnId));
        return _repo.CommitAsync(txnId, ct);
    }

    public Task<InjectorTxnResult> RollbackAsync(string txnId, CancellationToken ct)
    {
        RequireText(txnId, nameof(txnId));
        return _repo.RollbackAsync(txnId, ct);
    }

    public async Task<InjectorCleanupResult> CleanupPendingAsync(
        int timeoutMinutes,
        int maxBatch,
        CancellationToken ct)
    {
        var mins = Math.Max(1, timeoutMinutes);
        var limit = Math.Clamp(maxBatch, 1, 500);
        var ids = await _repo.ListExpiredPendingTxnIdsAsync(mins, limit, ct).ConfigureAwait(false);
        var cleaned = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var rolled = await _repo.RollbackAsync(id, ct).ConfigureAwait(false);
            if (rolled.Ok)
            {
                cleaned++;
            }
        }

        return new InjectorCleanupResult(Ok: true, cleaned);
    }

    public async Task<InjectorClaimResult> ClaimByTargetAsync(
        string clientId,
        string drugId,
        string spec,
        CancellationToken ct)
    {
        RequireText(clientId, nameof(clientId));
        RequireText(drugId, nameof(drugId));
        RequireText(spec, nameof(spec));
        var tasks = await _repo.ClaimByTargetAsync(clientId, drugId, spec, ct).ConfigureAwait(false);
        return new InjectorClaimResult(tasks);
    }

    public Task<bool> HasWarehouseSuccessAsync(
        string warehouseBillNo,
        string drugId,
        string spec,
        string? rowFingerprint,
        CancellationToken ct)
    {
        RequireText(warehouseBillNo, nameof(warehouseBillNo));
        RequireText(drugId, nameof(drugId));
        RequireText(spec, nameof(spec));
        return _repo.HasWarehouseSuccessAsync(warehouseBillNo, drugId, spec, rowFingerprint, ct);
    }

    public Task<IReadOnlyList<InjectorPendingCode>> GetPendingCodesAsync(long taskId, CancellationToken ct)
    {
        RequirePositiveId(taskId);
        return _repo.GetPendingCodesAsync(taskId, ct);
    }

    public Task UpdateTaskCodesAsync(
        long taskId,
        IReadOnlyList<string> leafCodes,
        string status,
        string? verifyResult,
        string? errMsg,
        CancellationToken ct)
    {
        RequirePositiveId(taskId);
        RequireText(status, nameof(status));
        return _repo.UpdateTaskCodesAsync(taskId, leafCodes, status, verifyResult, errMsg, ct);
    }

    public Task UpdateStagingStatusAsync(
        IReadOnlyList<long> stagingIds,
        string codeStatus,
        string? errMsg,
        CancellationToken ct)
    {
        RequireText(codeStatus, nameof(codeStatus));
        return _repo.UpdateStagingStatusAsync(stagingIds ?? [], codeStatus, errMsg, ct);
    }

    public Task InsertEventAsync(
        long taskId,
        string stage,
        string level,
        string message,
        string? leafCode,
        CancellationToken ct)
    {
        RequirePositiveId(taskId);
        RequireText(stage, nameof(stage));
        RequireText(level, nameof(level));
        RequireText(message, nameof(message));
        return _repo.InsertEventAsync(taskId, stage, level, message, leafCode, ct);
    }

    public async Task<InjectorFinalizeResult> FinalizeAsync(
        long taskId,
        string? errMsg,
        string? warehouseBillNo,
        string? rowFingerprint,
        CancellationToken ct)
    {
        RequirePositiveId(taskId);
        var rows = await _repo.FinalizeAsync(taskId, errMsg, warehouseBillNo, rowFingerprint, ct)
            .ConfigureAwait(false);
        return new InjectorFinalizeResult(rows);
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("required", name);
        }
    }

    private static void RequirePositiveId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "id must be positive");
        }
    }
}
