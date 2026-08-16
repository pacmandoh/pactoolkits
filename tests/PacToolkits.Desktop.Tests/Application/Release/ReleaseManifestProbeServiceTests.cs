using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class ReleaseManifestProbeServiceTests
{
    [Fact]
    public async Task Probe_allows_reachable_beta_feed()
    {
        var service = CreateService(Manifest("beta"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits/current",
            "beta",
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://updates.example/feed/pactoolkits/beta/release-manifest.json",
            result.FeedManifestUrl);
        Assert.Equal("1.0.0-beta.1", result.ManifestProductVersion);
        Assert.Contains("beta", result.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0-beta.1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_rejects_unsupported_channel_without_throwing()
    {
        var service = CreateService(Manifest("beta"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits",
            "preview",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("不支持的更新通道", result.Message, StringComparison.Ordinal);
        Assert.Contains("preview", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stable", "https://updates.example/feed/pactoolkits/current/release-manifest.json")]
    [InlineData("beta", "https://updates.example/feed/pactoolkits/beta/release-manifest.json")]
    public void ResolveChannelManifestUrl_accepts_supported_channels(string channel, string expected)
    {
        var url = ReleaseManifestProbeService.ResolveChannelManifestUrl(
            "https://updates.example/feed/pactoolkits/current",
            channel);

        Assert.Equal(expected, url);
    }

    [Fact]
    public void ResolveChannelManifestUrl_returns_empty_for_unsupported_channel()
    {
        var url = ReleaseManifestProbeService.ResolveChannelManifestUrl(
            "https://updates.example/feed/pactoolkits",
            "preview");

        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public void ResolveChannelManifestUrl_uses_path_combine_for_local_feed()
    {
        var root = Path.Combine(Path.GetTempPath(), "pactoolkits-probe-manifest-root");
        var expected = Path.Combine(root, "beta", "release-manifest.json");
        var url = ReleaseManifestProbeService.ResolveChannelManifestUrl(root, "beta");
        Assert.Equal(expected, url);
    }

    [Fact]
    public async Task Probe_allows_reachable_local_feed_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pactoolkits-local-feed-" + Guid.NewGuid().ToString("N"));
        var channelDir = Path.Combine(root, "beta");
        Directory.CreateDirectory(channelDir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(channelDir, "release-manifest.json"),
                Manifest("beta"),
                TestContext.Current.CancellationToken);

            var service = new ReleaseManifestProbeService(
                new NullLogger(),
                new HttpClient());

            var result = await service.ProbeAsync(
                root, "beta", TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.Message);
            Assert.Equal(Path.Combine(channelDir, "release-manifest.json"), result.FeedManifestUrl);
            Assert.Equal(Path.Combine(root, "beta"), result.TargetFeedUrl);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Probe_reports_missing_local_manifest_in_chinese()
    {
        var root = Path.Combine(Path.GetTempPath(), "pactoolkits-missing-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new ReleaseManifestProbeService(
                new NullLogger(),
                new HttpClient());

            var result = await service.ProbeAsync(
                root, "stable", TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Contains("未找到 release-manifest.json", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Probe_rejects_manifest_from_wrong_channel()
    {
        var service = CreateService(Manifest("stable"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits",
            "beta",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("通道不匹配", result.Message, StringComparison.Ordinal);
    }

    private static ReleaseManifestProbeService CreateService(string manifest)
    {
        var http = new HttpClient(new StaticResponseHandler(manifest));
        return new ReleaseManifestProbeService(new NullLogger(), http);
    }

    private static string Manifest(string channel)
        => $$"""
             {
               "product": { "version": "1.0.0-{{channel}}.1" },
               "release": { "channel": "{{channel}}" }
             }
             """;

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pactoolkits-release-manifest-probe-test.log";
        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
