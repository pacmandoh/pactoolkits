using System;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>Desktop 调 Pac API 失败；带 status、code、traceId 给页面和日志用</summary>
public sealed class PacApiException : Exception
{
    public PacApiException(PacApiProblem problem, Exception? inner = null)
        : base(FormatMessage(problem), inner)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
    }

    public PacApiProblem Problem { get; }

    public int Status => Problem.Status;

    public string? Code => Problem.Code;

    public string? TraceId => Problem.TraceId;

    public TimeSpan? RetryAfter => Problem.RetryAfter;

    /// <summary>瞬时失败：页面 Stale 重试；不要当成业务 LoadFailed</summary>
    public bool IsTransient
        => Status is 408 or 429 or 502 or 503 or 504
           || string.Equals(Code, "transport", StringComparison.Ordinal);

    private static string FormatMessage(PacApiProblem problem)
    {
        var code = string.IsNullOrWhiteSpace(problem.Code) ? "api_error" : problem.Code;
        var title = string.IsNullOrWhiteSpace(problem.Title) ? "API request failed" : problem.Title;
        return $"HTTP {problem.Status} {code}: {title}";
    }
}
