namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 探测目标发布通道 manifest 与数据库 Schema 兼容性
/// </summary>
public interface IReleaseManifestProbeService
{
    Task<ReleaseManifestProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        PgOptions pgOptions,
        CancellationToken ct = default);
}
