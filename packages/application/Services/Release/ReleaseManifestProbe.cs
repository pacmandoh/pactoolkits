using System.Text.Json;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// 探测目标通道 release-manifest：Feed 可达、通道一致、读出 product.version
/// </summary>
public sealed class ReleaseManifestProbeService : IReleaseManifestProbeService
{
    public const string HttpClientName = "pac-release-manifest";

    private readonly IAppLogger _logger;
    private readonly HttpClient _http;

    public ReleaseManifestProbeService(IAppLogger logger, HttpClient http)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<ReleaseManifestProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
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
            if (AppUpdatePolicy.TryGetLocalFeedPath(baseFeedUrl, out _))
            {
                manifest = await ReadLocalManifestAsync(manifestUrl, ct).ConfigureAwait(false);
            }
            else
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
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed(channel, targetFeedUrl, manifestUrl, "目标更新源检查超时");
        }
        catch (FileNotFoundException)
        {
            return Failed(channel, targetFeedUrl, manifestUrl, "目标更新源不可用：未找到 release-manifest.json");
        }
        catch (DirectoryNotFoundException)
        {
            return Failed(channel, targetFeedUrl, manifestUrl, "目标更新源不可用：更新源目录不存在");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(channel, targetFeedUrl, manifestUrl, "目标更新源不可用：无权限读取更新源");
        }
        catch (IOException ex)
        {
            _logger.Warn("ReleaseManifestProbe", "update.channel.feed_probe_fail",
                "Failed probing local release channel feed", ex, new { channel, manifestUrl });
            return Failed(channel, targetFeedUrl, manifestUrl, $"目标更新源不可用：{ex.Message}");
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

        return new ReleaseManifestProbe(
            true,
            channel,
            targetFeedUrl,
            manifestUrl,
            manifest.ProductVersion,
            $"目标 {channel} Feed 可用（{manifest.ProductVersion}）");
    }

    internal static string ResolveChannelManifestUrl(string? baseFeedUrl, string channel)
    {
        if (!AppUpdatePolicy.TryNormalizeChannel(channel, out var normalizedChannel))
        {
            return string.Empty;
        }

        var feedUrl = AppUpdatePolicy.ResolveFeedUrl(baseFeedUrl, normalizedChannel);
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return string.Empty;
        }

        return AppUpdatePolicy.TryGetLocalFeedPath(baseFeedUrl, out _)
            ? Path.Combine(feedUrl, "release-manifest.json")
            : $"{feedUrl}/release-manifest.json";
    }

    private static async Task<ChannelManifest> ReadLocalManifestAsync(string manifestPath, CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("release-manifest.json not found", manifestPath);
        }

        var info = new FileInfo(manifestPath);
        if (info.Length > 2 * 1024 * 1024)
        {
            throw new InvalidOperationException("release-manifest.json exceeds 2 MiB");
        }

        ct.ThrowIfCancellationRequested();
        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        // 本地文件先卡大小再同步 Parse；Parse 不看 token，别挂假 CancelAfter
        return ReadManifest(stream);
    }

    private static ChannelManifest ReadManifest(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var productVersion = ReadRequiredString(root.GetProperty("product"), "version");
        var channel = ReadRequiredString(root.GetProperty("release"), "channel");
        return new ChannelManifest(NormalizeManifestChannel(channel), productVersion);
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
        => new(false, channel, feedUrl, manifestUrl, string.Empty, message);

    private sealed record ChannelManifest(string Channel, string ProductVersion);
}
