using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>读取业务变更水位表</summary>
public interface IChangeWatermarkRepo
{
    Task<IReadOnlyList<ChangeWatermarkItem>> ListAsync(CancellationToken ct = default);
}
