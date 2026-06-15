using System.Net;
using System.Net.Http;
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
        var service = CreateService("1.2.23", Manifest("beta", "1.2.22", "1.2.24"));

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
    {
        var http = new HttpClient(new StaticResponseHandler(manifest));
        return new ReleaseChannelSwitchService(
            new FakeDbSchemaVersionService(dbVersion),
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
        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionReadResult(true, version, null));

        public Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionReadResult(true, version, null));
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
