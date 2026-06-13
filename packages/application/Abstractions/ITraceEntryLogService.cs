using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface ITraceEntryLogService
{
    Task WriteAsync(TraceEntryLogDto dto, CancellationToken ct = default);
}
