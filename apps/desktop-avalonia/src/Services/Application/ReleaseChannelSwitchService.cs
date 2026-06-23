using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed record ReleaseChannelSwitchProbe(
    bool Success,
    string TargetChannel,
    string FeedManifestUrl,
    string? CurrentDbSchema,
    string RequiredMinDbSchema,
    string RequiredMaxDbSchema,
    string Message);

public interface IReleaseChannelSwitchService
{
    Task<ReleaseChannelSwitchProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        PgOptions pgOptions,
        CancellationToken ct = default);
}

public sealed class ReleaseChannelSwitchService : IReleaseChannelSwitchService
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IAppLogger _logger;
    private readonly HttpClient _http;

    public ReleaseChannelSwitchService(
        IDbSchemaVersionService dbSchemaVersion,
        IAppLogger logger)
        : this(dbSchemaVersion, logger, SharedHttp)
    {
    }

    internal ReleaseChannelSwitchService(
        IDbSchemaVersionService dbSchemaVersion,
        IAppLogger logger,
        HttpClient http)
    {
        _dbSchemaVersion = dbSchemaVersion;
        _logger = logger;
        _http = http;
    }

    public async Task<ReleaseChannelSwitchProbe> ProbeAsync(
        string? baseFeedUrl,
        string targetChannel,
        PgOptions pgOptions,
        CancellationToken ct = default)
    {
        if (!TryNormalizeChannel(targetChannel, out var channel))
        {
            return Failed(string.Empty, string.Empty, $"不支持的更新通道：{targetChannel}");
        }

        var manifestUrl = ResolveChannelManifestUrl(baseFeedUrl, channel);
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return Failed(channel, manifestUrl, "未配置更新源地址");
        }

        ChannelManifest manifest;
        try
        {
            using var response = await _http.GetAsync(manifestUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(channel, manifestUrl, $"目标更新源不可用：HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            manifest = ReadManifest(stream);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed(channel, manifestUrl, "目标更新源检查超时");
        }
        catch (Exception ex)
        {
            _logger.Warn("ReleaseChannelSwitch", "update.channel.feed_probe_fail",
                "Failed probing target release channel feed", ex, new { channel, manifestUrl });
            return Failed(channel, manifestUrl, $"目标更新源不可用：{ex.Message}");
        }

        if (!string.Equals(manifest.Channel, channel, StringComparison.Ordinal))
        {
            return Failed(channel, manifestUrl,
                $"更新源通道不匹配：请求 {channel}，清单为 {manifest.Channel}");
        }

        var schema = await _dbSchemaVersion.TryReadSchemaVersionAsync(pgOptions, ct).ConfigureAwait(false);
        if (!schema.Ok)
        {
            return Failed(channel, manifestUrl, schema.Reason ?? "无法读取当前数据库版本");
        }

        var compatibility = DbSchemaCompat.Evaluate(
            schema.Value,
            manifest.RequiredMinDbSchema,
            manifest.RequiredMaxDbSchema);
        if (!compatibility.IsCompatible)
        {
            return new ReleaseChannelSwitchProbe(
                false,
                channel,
                manifestUrl,
                schema.Value,
                manifest.RequiredMinDbSchema,
                manifest.RequiredMaxDbSchema,
                compatibility.Message);
        }

        return new ReleaseChannelSwitchProbe(
            true,
            channel,
            manifestUrl,
            schema.Value,
            manifest.RequiredMinDbSchema,
            manifest.RequiredMaxDbSchema,
            $"目标 {channel} Feed 可用，数据库 {schema.Value} 位于支持范围 " +
            $"{manifest.RequiredMinDbSchema} - {manifest.RequiredMaxDbSchema}");
    }

    internal static string ResolveChannelManifestUrl(string? baseFeedUrl, string channel)
    {
        if (!TryNormalizeChannel(channel, out var normalizedChannel))
        {
            return string.Empty;
        }

        var normalizedBase = string.IsNullOrWhiteSpace(baseFeedUrl)
            ? string.Empty
            : baseFeedUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
        {
            return string.Empty;
        }

        if (normalizedBase.EndsWith("/stable", StringComparison.OrdinalIgnoreCase)
            || normalizedBase.EndsWith("/beta", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = normalizedBase.LastIndexOf('/');
            if (lastSlash > 0)
            {
                normalizedBase = normalizedBase[..lastSlash];
            }
        }

        return $"{normalizedBase}/{normalizedChannel}/release-manifest.json";
    }

    private static ChannelManifest ReadManifest(System.IO.Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var channel = ReadRequiredString(root.GetProperty("release"), "channel");
        var components = root.GetProperty("components");
        var desktop = components.GetProperty("desktop");

        var minimums = new List<string> { ReadRequiredString(desktop, "minDbSchema") };
        var maximums = new List<string> { ReadRequiredString(desktop, "maxDbSchema") };
        if (desktop.TryGetProperty("bundles", out var bundles))
        {
            foreach (var bundle in bundles.EnumerateArray())
            {
                var id = bundle.GetString();
                if (string.IsNullOrWhiteSpace(id) || !components.TryGetProperty(id, out var component))
                {
                    throw new InvalidOperationException($"更新清单引用了不存在的组件：{id}");
                }

                minimums.Add(ReadRequiredString(component, "minDbSchema"));
                maximums.Add(ReadRequiredString(component, "maxDbSchema"));
            }
        }

        var requiredMin = minimums[0];
        for (var i = 1; i < minimums.Count; i++)
        {
            requiredMin = DbSchemaCompat.GetRequiredMin(requiredMin, minimums[i]);
        }

        var requiredMax = maximums[0];
        for (var i = 1; i < maximums.Count; i++)
        {
            requiredMax = DbSchemaCompat.GetRequiredMax(requiredMax, maximums[i]);
        }

        return new ChannelManifest(NormalizeManifestChannel(channel), requiredMin, requiredMax);
    }

    private static string NormalizeManifestChannel(string channel)
    {
        if (!TryNormalizeChannel(channel, out var normalized))
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

    internal static bool TryNormalizeChannel(string? channel, out string normalized)
    {
        normalized = (channel ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "stable" or "beta")
        {
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static ReleaseChannelSwitchProbe Failed(string channel, string url, string message)
        => new(false, channel, url, null, "unknown", "unknown", message);

    private sealed record ChannelManifest(
        string Channel,
        string RequiredMinDbSchema,
        string RequiredMaxDbSchema);
}
