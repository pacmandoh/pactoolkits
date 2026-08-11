namespace PacToolkits.Application.Abstractions;

public enum AppLogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    Fatal = 4
}

/// <summary>
/// 定义按模块和事件写入结构化日志及导出近期日志的契约
/// </summary>
public interface IAppLogger
{
    string LogDirectory { get; }
    string CurrentLogPath { get; }

    void Debug(string module, string eventName, string message, object? context = null, string? traceId = null);
    void Info(string module, string eventName, string message, object? context = null, string? traceId = null);
    void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null);
    void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null);
    void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null);
    Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default);
}
