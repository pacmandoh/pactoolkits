using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.DTOs;

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

public sealed record DrugIndexSaveResult(
    DrugSaveOutcome Outcome,
    DrugIndexDto? Saved,
    DrugIndexConcurrencyException? Concurrency);

public sealed record DrugKeyFixRequest(
    DrugIndexDto Source,
    DrugIndexDto Target,
    string Reason,
    string OperatorName,
    string SourceTag);

public sealed record DrugKeyFixCommitResult(
    DrugKeyFixApplyResultDto Apply,
    DrugIndexDto? SourceAfter,
    DrugIndexDto? TargetAfter);

public sealed record DrugIndexQuery(string? Keyword);

public sealed record DrugIndexSearchResult(
    IReadOnlyList<DrugIndexDto> Items,
    int TotalCount);
