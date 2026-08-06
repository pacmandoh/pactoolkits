using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Core;

namespace PacToolkits.Application.Services;

/// <summary>
/// 探测发布通道 manifest 身份与本地 Schema 兼容范围
/// </summary>
public sealed class ReleaseManifestProbeService : IReleaseManifestProbeService
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IAppLogger _logger;
    private readonly HttpClient _http;

    public ReleaseManifestProbeService(
        IDbSchemaVersionService dbSchemaVersion,
        IAppLogger logger)
        : this(dbSchemaVersion, logger, SharedHttp)
    {
    }

    internal ReleaseManifestProbeService(
        IDbSchemaVersionService dbSchemaVersion,
        IAppLogger logger,
        HttpClient http)
    {
        _dbSchemaVersion = dbSchemaVersion;
        _logger = logger;
        _http = http;
    }

    public async Task<ReleaseManifestProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        PgOptions pgOptions,
        CancellationToken ct = default)
    {
        if (!AppUpdatePolicy.TryNormalizeChannel(targetChannel, out var channel))
        {
            return Failed(string.Empty, string.Empty, string.Empty, $"不支持的更新通道：{targetChannel}");
        }

        var targetFeedUrl = AppUpdatePolicy.ResolveFeedUrl(baseFeedUrl, channel);
        var manifestUrl = ResolveChannelManifestUrl(baseFeedUrl, channel);
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return Failed(channel, targetFeedUrl, manifestUrl, "未配置更新源地址");
        }

        ChannelManifest manifest;
        try
        {
            using var response = await _http.GetAsync(manifestUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(channel, targetFeedUrl, manifestUrl,
                    $"目标更新源不可用：HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            manifest = ReadManifest(stream);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed(channel, targetFeedUrl, manifestUrl, "目标更新源检查超时");
        }
        catch (Exception ex)
        {
            _logger.Warn("ReleaseManifestProbe", "update.channel.feed_probe_fail",
                "Failed probing target release channel feed", ex, new { channel, manifestUrl });
            return Failed(channel, targetFeedUrl, manifestUrl, $"目标更新源不可用：{ex.Message}");
        }

        if (!string.Equals(manifest.Channel, channel, StringComparison.Ordinal))
        {
            return Failed(channel, targetFeedUrl, manifestUrl,
                $"更新源通道不匹配：请求 {channel}，清单为 {manifest.Channel}");
        }

        var schema = await _dbSchemaVersion.TryReadSchemaVersionAsync(pgOptions, ct).ConfigureAwait(false);
        if (!schema.Ok)
        {
            return Failed(channel, targetFeedUrl, manifestUrl, schema.Reason ?? "无法读取当前数据库版本");
        }

        var compatibility = DbSchemaCompat.Evaluate(
            schema.Value,
            manifest.RequiredMinDbSchema,
            manifest.RequiredMaxDbSchema);
        if (!compatibility.IsCompatible)
        {
            return new ReleaseManifestProbe(
                false,
                channel,
                targetFeedUrl,
                manifestUrl,
                manifest.ProductVersion,
                schema.Value,
                manifest.RequiredMinDbSchema,
                manifest.RequiredMaxDbSchema,
                compatibility.Message);
        }

        return new ReleaseManifestProbe(
            true,
            channel,
            targetFeedUrl,
            manifestUrl,
            manifest.ProductVersion,
            schema.Value,
            manifest.RequiredMinDbSchema,
            manifest.RequiredMaxDbSchema,
            $"目标 {channel} Feed 可用，数据库 {schema.Value} 位于支持范围 " +
            $"{manifest.RequiredMinDbSchema} - {manifest.RequiredMaxDbSchema}");
    }

    internal static string ResolveChannelManifestUrl(string? baseFeedUrl, string channel)
    {
        if (!AppUpdatePolicy.TryNormalizeChannel(channel, out var normalizedChannel))
        {
            return string.Empty;
        }

        var feedUrl = AppUpdatePolicy.ResolveFeedUrl(baseFeedUrl, normalizedChannel);
        return string.IsNullOrWhiteSpace(feedUrl)
            ? string.Empty
            : $"{feedUrl}/release-manifest.json";
    }

    private static ChannelManifest ReadManifest(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var productVersion = ReadRequiredString(root.GetProperty("product"), "version");
        var channel = ReadRequiredString(root.GetProperty("release"), "channel");
        var components = root.GetProperty("components");
        var desktop = ReleaseManifestDesktop.GetRequiredAvalonia(components);

        return new ChannelManifest(
            NormalizeManifestChannel(channel),
            productVersion,
            ReadRequiredString(desktop, "minDbSchema"),
            ReadRequiredString(desktop, "maxDbSchema"));
    }

    private static string NormalizeManifestChannel(string channel)
    {
        if (!AppUpdatePolicy.TryNormalizeChannel(channel, out var normalized))
        {
            throw new InvalidOperationException($"更新清单通道无效：{channel}");
        }

        return normalized;
    }

    private static string ReadRequiredString(JsonElement element, string property)
    {
        var value = element.TryGetProperty(property, out var child) ? child.GetString()?.Trim() : null;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"更新清单缺少字段：{property}")
            : value;
    }

    private static ReleaseManifestProbe Failed(string channel, string feedUrl, string manifestUrl, string message)
        => new(false, channel, feedUrl, manifestUrl, string.Empty,
            null, "unknown", "unknown", message);

    private sealed record ChannelManifest(
        string Channel,
        string ProductVersion,
        string RequiredMinDbSchema,
        string RequiredMaxDbSchema);
}
