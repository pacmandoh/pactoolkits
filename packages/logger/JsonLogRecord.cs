namespace PacToolkits.Logger;

/// <summary>
/// JSON Lines 单行日志记录（与 Desktop / Host / AHK 字段约定一致）
/// </summary>
public sealed class JsonLogRecord
{
    public DateTimeOffset Ts { get; init; }

    public string Level { get; init; } = string.Empty;

    public string Module { get; init; } = string.Empty;

    public string Event { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string Version { get; init; } = string.Empty;

    public object? Context { get; init; }

    public JsonLogException? Exception { get; init; }
}

/// <summary>可选异常载荷</summary>
public sealed class JsonLogException
{
    public string Type { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? StackTrace { get; init; }
}
