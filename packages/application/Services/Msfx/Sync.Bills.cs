using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class SyncService
{
    public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetDueBillRetriesAsync(sourceApi, NormalizeLimit(limit), ct);
    }

    public Task UpsertBillRetryAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? lastError, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.UpsertBillRetryAsync(sourceApi, billCode.Trim(), fromRefUserId, toRefUserId, lastError, ct);
    }

    public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.MarkBillRetrySucceededAsync(sourceApi, billCode.Trim(), ct);
    }

    public Task UpsertBillWatchAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? fromEntName, string? billType, string? billTime, string? billUploadTime, string? lastSeenStatus, string? rawJson, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.UpsertBillWatchAsync(sourceApi, billCode.Trim(), fromRefUserId, toRefUserId, fromEntName, billType, billTime, billUploadTime, lastSeenStatus, rawJson, ct);
    }

    public Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetDueBillWatchesAsync(sourceApi, NormalizeLimit(limit), ct);
    }

    public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.MarkBillWatchResolvedAsync(sourceApi, billCode.Trim(), ct);
    }

    public Task RescheduleBillWatchAsync(string sourceApi, string billCode, string? lastSeenStatus, string? lastError, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.RescheduleBillWatchAsync(sourceApi, billCode.Trim(), lastSeenStatus, lastError, ct);
    }

    public Task<long> UpsertInboundBillAsync(long batchId, string billCode, string billType, string billTime, string billUploadTime, string fromRefUserId, string fromEntName, string toRefUserId, string toUserId, string toUserName, string status, string rawJson, CancellationToken ct)
    {
        RequirePositiveId(batchId, nameof(batchId));
        RequireText(billCode, nameof(billCode));
        RequireText(status, nameof(status));
        return _repo.UpsertInboundBillAsync(batchId, billCode.Trim(), billType, billTime, billUploadTime, fromRefUserId, fromEntName, toRefUserId, toUserId, toUserName, status.Trim(), rawJson, ct);
    }

    public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(long billId, string billCode, IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs, CancellationToken ct)
    {
        RequirePositiveId(billId, nameof(billId));
        RequireText(billCode, nameof(billCode));
        ArgumentNullException.ThrowIfNull(drugs);
        return _repo.IngestUpoutDetailAsync(billId, billCode.Trim(), drugs, ct);
    }

}
