namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 发布通道探测结果（Feed、Schema 门槛与是否可切通道）
/// </summary>
public sealed record ReleaseChannelProbe(
    bool Success,
    string TargetChannel,
    string FeedManifestUrl,
    string? CurrentDbSchema,
    string RequiredMinDbSchema,
    string RequiredMaxDbSchema,
    string Message);

/// <summary>
/// 探测目标发布通道与数据库 Schema 兼容性
/// </summary>
public interface IReleaseChannelService
{
    Task<ReleaseChannelProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        PgOptions pgOptions,
        CancellationToken ct = default);
}
