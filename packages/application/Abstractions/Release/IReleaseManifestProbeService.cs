namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 探测目标发布通道的 release-manifest：Feed 可达、通道一致、能读出 product.version
/// </summary>
public interface IReleaseManifestProbeService
{
    Task<ReleaseManifestProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        CancellationToken ct = default);
}
