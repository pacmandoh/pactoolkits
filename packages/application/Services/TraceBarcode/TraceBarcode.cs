using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

/// <summary>校验条码取码与审计请求，并按药品规格完成库存预留</summary>
public sealed class TraceBarcodeService(ITraceBarcodeRepo repo) : ITraceBarcodeService
{
    public event Action? AuditCleared;

    public const int MaxPickItems = 50;
    public const int MaxCountPerItem = 999;
    public const int MaxTotalPickCount = 2000;
    public const int MaxPreviewLabels = 200;
    internal const int MaxAuditItems = MaxTotalPickCount;
    internal const int MaxTraceCodeLength = 256;
    internal const int MaxDrugFieldLength = 128;
    internal const int MaxFileNameLength = 260;
    internal const int MaxErrorLength = 2000;
    internal const int MaxOperatorNameLength = 256;

    public async Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items is null || request.Items.Count == 0)
        {
            return new TraceBarcodePickResult(Array.Empty<TraceBarcodePickGroup>());
        }

        if (request.BatchId == Guid.Empty)
        {
            throw new ArgumentException("batchId must be non-empty");
        }

        var operatorName = RequireWithinLength(request.OperatorName, "operatorName", MaxOperatorNameLength);

        if (request.Items.Count > MaxPickItems)
        {
            throw new ArgumentException($"pick items cannot exceed {MaxPickItems}");
        }

        var merged = new Dictionary<(string DrugId, string Spec), int>();
        foreach (var item in request.Items)
        {
            ArgumentNullException.ThrowIfNull(item);
            var drugId = RequireWithinLength(item.DrugId, "drugId", MaxDrugFieldLength);
            var spec = RequireWithinLength(item.Spec, "spec", MaxDrugFieldLength);
            if (item.Count <= 0)
            {
                throw new ArgumentException("pick count must be greater than zero");
            }

            var count = item.Count;
            if (count > MaxCountPerItem)
            {
                throw new ArgumentException($"pick count cannot exceed {MaxCountPerItem}");
            }

            var key = (drugId, spec);
            var mergedCount = merged.GetValueOrDefault(key) + count;
            if (mergedCount > MaxCountPerItem)
            {
                throw new ArgumentException($"merged pick count cannot exceed {MaxCountPerItem}");
            }

            merged[key] = mergedCount;
        }

        var total = merged.Values.Sum();
        if (total > MaxTotalPickCount)
        {
            throw new ArgumentException($"total pick count cannot exceed {MaxTotalPickCount}");
        }

        var excludeRecentDays = NormalizeExcludeRecentDays(request.ExcludeRecentDays);
        var exclude = new HashSet<string>(NormalizeExclude(request.ExcludeTraceCodes), StringComparer.Ordinal);
        var groups = new List<TraceBarcodePickGroup>(merged.Count);
        foreach (var ((drugId, spec), count) in merged)
        {
            var group = await repo.PickAsync(
                    request.BatchId,
                    operatorName,
                    drugId,
                    spec,
                    count,
                    excludeRecentDays,
                    exclude.ToArray(),
                    ct)
                .ConfigureAwait(false);
            groups.Add(group);

            foreach (var code in group.Codes)
            {
                exclude.Add(code.TraceCode);
            }
        }

        return new TraceBarcodePickResult(groups);
    }

    public Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items is null || request.Items.Count == 0)
        {
            return Task.FromResult(0);
        }

        if (request.Items.Count > MaxAuditItems)
        {
            throw new ArgumentException($"audit items cannot exceed {MaxAuditItems}");
        }

        if (request.BatchId == Guid.Empty)
        {
            throw new ArgumentException("batchId must be non-empty");
        }

        var action = InputNormalizer.Normalize(request.Action);
        if (action is not ("preview" or "export"))
        {
            throw new ArgumentException("action must be preview or export");
        }

        var operatorName = RequireWithinLength(request.OperatorName, "operatorName", MaxOperatorNameLength);
        var items = new List<TraceBarcodeAuditItem>(request.Items.Count);
        foreach (var item in request.Items)
        {
            items.Add(NormalizeAuditItem(item));
        }

        return repo.InsertAuditAsync(
            request with { Action = action, OperatorName = operatorName, Items = items },
            ct);
    }

    public async Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null)
    {
        var deleted = await repo.ClearAuditLogAsync(ct).ConfigureAwait(true);
        AuditCleared?.Invoke();
        return deleted;
    }

    private static int? NormalizeExcludeRecentDays(int? excludeRecentDays)
    {
        if (!excludeRecentDays.HasValue)
        {
            return null;
        }

        return Math.Clamp(excludeRecentDays.Value, 0, BarcodeGenOptions.MaxExcludeRecentDays);
    }

    private static TraceBarcodeAuditItem NormalizeAuditItem(TraceBarcodeAuditItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var traceCode = RequireWithinLength(item.TraceCode, "traceCode", MaxTraceCodeLength);
        var drugId = TrimOptional(item.DrugId, MaxDrugFieldLength);
        var spec = TrimOptional(item.Spec, MaxDrugFieldLength);
        var fileName = TrimOptional(item.FileName, MaxFileNameLength);
        var error = TrimOptional(item.Error, MaxErrorLength);
        if (!item.Success && error is null)
        {
            throw new ArgumentException("error is required when success is false");
        }

        return new TraceBarcodeAuditItem(traceCode, drugId, spec, fileName, item.Success, error);
    }

    private static string RequireWithinLength(string? value, string fieldName, int maxLength)
    {
        var normalized = InputNormalizer.Normalize(value)
            ?? throw new ArgumentException($"{fieldName} is required");
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} cannot exceed {maxLength} characters");
        }

        return normalized;
    }

    private static string? TrimOptional(string? value, int maxLength)
    {
        var normalized = InputNormalizer.Normalize(value);
        return normalized is null ? null : TrimTo(normalized, maxLength);
    }

    private static string TrimTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static IReadOnlyList<string> NormalizeExclude(IReadOnlyList<string>? codes)
    {
        if (codes is null || codes.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (codes.Count > MaxTotalPickCount)
        {
            throw new ArgumentException($"exclude trace codes cannot exceed {MaxTotalPickCount}");
        }

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var value = InputNormalizer.Normalize(code);
            if (value is null)
            {
                continue;
            }

            if (value.Length > MaxTraceCodeLength)
            {
                throw new ArgumentException($"exclude trace code cannot exceed {MaxTraceCodeLength} characters");
            }

            normalized.Add(value);
        }

        return normalized.ToArray();
    }
}
