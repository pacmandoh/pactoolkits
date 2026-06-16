using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class ReleaseChannelSwitchServiceTests
{
    [Fact]
    public async Task Probe_allows_compatible_beta_feed()
    {
        var schema = new FakeDbSchemaVersionService("1.2.23");
        var service = CreateService(schema, Manifest("beta", "1.2.22", "1.2.24"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits/stable",
            "beta",
            new PgOptions(),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://updates.example/feed/pactoolkits/beta/release-manifest.json",
            result.FeedManifestUrl);
        Assert.Equal("1.2.22", result.RequiredMinDbSchema);
        Assert.Equal("1.2.24", result.RequiredMaxDbSchema);
        Assert.Equal("1.2.23", schema.Version);
        Assert.Equal(1, schema.ReadCount);
    }

    [Fact]
    public async Task Probe_blocks_beta_to_stable_when_database_is_above_maximum()
    {
        var service = CreateService("1.2.24", Manifest("stable", "1.2.20", "1.2.23"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits",
            "stable",
            new PgOptions(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("高于当前程序支持范围", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_blocks_stable_to_beta_when_database_is_below_minimum()
    {
        var service = CreateService("1.2.20", Manifest("beta", "1.2.22", "1.2.24"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits/stable",
            "beta",
            new PgOptions(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("低于最低支持版本", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_allows_beta_to_stable_when_database_is_compatible()
    {
        var service = CreateService("1.2.22", Manifest("stable", "1.2.20", "1.2.23"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits/beta",
            "stable",
            new PgOptions(),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://updates.example/feed/pactoolkits/stable/release-manifest.json",
            result.FeedManifestUrl);
    }

    [Fact]
    public async Task Probe_rejects_unsupported_channel_without_throwing()
    {
        var service = CreateService("1.2.23", Manifest("beta", "1.2.22", "1.2.24"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits",
            "preview",
            new PgOptions(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("不支持的更新通道", result.Message, StringComparison.Ordinal);
        Assert.Contains("preview", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stable", "https://updates.example/feed/pactoolkits/stable/release-manifest.json")]
    [InlineData("beta", "https://updates.example/feed/pactoolkits/beta/release-manifest.json")]
    public void ResolveChannelManifestUrl_accepts_supported_channels(string channel, string expected)
    {
        var url = ReleaseChannelSwitchService.ResolveChannelManifestUrl(
            "https://updates.example/feed/pactoolkits/stable",
            channel);

        Assert.Equal(expected, url);
    }

    [Fact]
    public void ResolveChannelManifestUrl_returns_empty_for_unsupported_channel()
    {
        var url = ReleaseChannelSwitchService.ResolveChannelManifestUrl(
            "https://updates.example/feed/pactoolkits",
            "preview");

        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public async Task Probe_rejects_manifest_from_wrong_channel()
    {
        var service = CreateService("1.2.23", Manifest("stable", "1.2.20", "1.2.23"));

        var result = await service.ProbeAsync(
            "https://updates.example/feed/pactoolkits",
            "beta",
            new PgOptions(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("通道不匹配", result.Message, StringComparison.Ordinal);
    }

    private static ReleaseChannelSwitchService CreateService(string dbVersion, string manifest)
        => CreateService(new FakeDbSchemaVersionService(dbVersion), manifest);

    private static ReleaseChannelSwitchService CreateService(
        FakeDbSchemaVersionService schema,
        string manifest)
    {
        var http = new HttpClient(new StaticResponseHandler(manifest));
        return new ReleaseChannelSwitchService(
            schema,
            new NullLogger(),
            http);
    }

    private static string Manifest(string channel, string min, string max)
        => $$"""
             {
               "release": { "channel": "{{channel}}" },
               "components": {
                 "desktop": {
                   "minDbSchema": "{{min}}",
                   "maxDbSchema": "{{max}}",
                   "bundles": ["agent-injector-ahk"]
                 },
                 "agent-injector-ahk": {
                   "minDbSchema": "{{min}}",
                   "maxDbSchema": "{{max}}"
                 }
               }
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

    private sealed class FakeDbSchemaVersionService(string version) : IDbSchemaVersionService
    {
        public string Version { get; } = version;

        public int ReadCount { get; private set; }

        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(new DbSchemaVersionReadResult(true, Version, null));
        }

        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(new DbSchemaVersionReadResult(true, Version, null));
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pactoolkits-channel-switch-test.log";
        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
