namespace PacToolkits.Application.DTOs;

public sealed record ScanCodeInsertResult(int RequestedCount, int InsertedCount, int SkippedCount);

public sealed record CodeAnalysis(
    int Total,
    int Invalid,
    int Duplicate,
    IReadOnlyList<string> ValidUniqueCodes);
