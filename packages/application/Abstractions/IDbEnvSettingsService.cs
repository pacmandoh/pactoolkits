using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 读取库内环境设置（production / isolated 等，影响迁移策略）
/// </summary>
public interface IDbEnvSettingsService
{
    Task<DbEnvSettings> TryReadAsync(CancellationToken ct);

    Task<DbEnvSettings> TryReadAsync(PgOptions options, CancellationToken ct);
}
