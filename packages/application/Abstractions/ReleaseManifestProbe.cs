namespace PacToolkits.Application.Abstractions;

/// <summary>目标发布版本、Manifest 身份与数据库兼容探测结果</summary>
public sealed record ReleaseManifestProbe(
    bool Success,
    string TargetChannel,
    string TargetFeedUrl,
    string FeedManifestUrl,
    string ManifestProductVersion,
    string? CurrentDbSchema,
    string RequiredMinDbSchema,
    string RequiredMaxDbSchema,
    string Message);
