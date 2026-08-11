namespace PacToolkits.Application.DTOs;

/// <summary>药品保存 HTTP 成功体；Blocked* 看 outcome</summary>
public sealed record DrugIndexSaveResponse(
    DrugSaveOutcome Outcome,
    DrugIndexDto? Saved);

public sealed record DrugKeyFixPreviewRequest(
    string SourceDrugId,
    string SourceSpec,
    string TargetDrugId,
    string TargetSpec);
