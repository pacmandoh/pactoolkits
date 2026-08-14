namespace PacToolkits.Application.Abstractions;

/// <summary>目标通道 Feed 清单探测结果：通道与 product.version</summary>
public sealed record ReleaseManifestProbe(
    bool Success,
    string TargetChannel,
    string TargetFeedUrl,
    string FeedManifestUrl,
    string ManifestProductVersion,
    string Message);
