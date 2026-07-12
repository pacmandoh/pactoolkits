namespace PacToolkits.Application.Abstractions;

public sealed record ReleaseChannelProbe(
    bool Success,
    string TargetChannel,
    string FeedManifestUrl,
    string? CurrentDbSchema,
    string RequiredMinDbSchema,
    string RequiredMaxDbSchema,
    string Message);

public interface IReleaseChannelService
{
    Task<ReleaseChannelProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        PgOptions pgOptions,
        CancellationToken ct = default);
}
