using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.DTOs;

/// <summary>
/// 药品索引保存请求（含主键变更与乐观锁版本）
/// </summary>
public sealed record DrugIndexSaveRequest(
    DrugIndexDto Dto,
    string? OriginDrugId,
    string? OriginSpec,
    long? ExpectedVersion,
    bool IsNew,
    bool HasPrimaryKeyChanges,
    bool HasQtyChanged);

public enum DrugSaveOutcome
{
    Saved,
    BlockedPrimaryKeyChange,
    BlockedQtyChange,
    BlockedDuplicate,
    ConcurrencyConflict
}

/// <summary>
/// 药品索引保存结果（含冲突快照）
/// </summary>
public sealed record DrugIndexSaveResult(
    DrugSaveOutcome Outcome,
    DrugIndexDto? Saved,
    DrugIndexConcurrencyException? Concurrency);

/// <summary>
/// 药品主键修复提交请求
/// </summary>
public sealed record DrugKeyFixRequest(
    DrugIndexDto Source,
    DrugIndexDto Target,
    string Reason,
    string OperatorName,
    string SourceTag);

/// <summary>
/// 主键修复提交后的源/目标快照
/// </summary>
public sealed record DrugKeyFixCommitResult(
    DrugKeyFixApplyResultDto Apply,
    DrugIndexDto? SourceAfter,
    DrugIndexDto? TargetAfter);

public sealed record DrugIndexQuery(string? Keyword);

public sealed record DrugIndexSearchResult(
    IReadOnlyList<DrugIndexDto> Items,
    int TotalCount);
