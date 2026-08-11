namespace PacToolkits.Application.DTOs;

/// <summary>ProblemDetails 里 Desktop 需要的字段</summary>
public sealed record PacApiProblem(
    int Status,
    string? Code,
    string? Title,
    string? Detail,
    string? TraceId,
    TimeSpan? RetryAfter,
    long? CurrentVersion = null);
