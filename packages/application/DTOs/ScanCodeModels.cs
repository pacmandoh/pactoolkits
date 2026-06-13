namespace PacToolkits.Application.DTOs;

public sealed record TraceCodeValidationRule(int RequiredLength, string Pattern);

public sealed record ScanCodeSubmitRequest(
    string DrugId,
    string Spec,
    IReadOnlyList<string> ValidUniqueCodes,
    CodeAnalysis Analysis,
    string ClientRaw,
    string Source = "manual");

public sealed record ScanCodeSubmitResult(
    bool DrugFound,
    ScanCodeInsertResult Insert,
    int QtyPerTrace,
    string EntryResult,
    string EntryMessage);
