using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 写入追溯码录入流水
/// </summary>
public interface ITraceEntryLogService
{
    Task WriteAsync(TraceEntryLogDto dto, CancellationToken ct = default);
}
