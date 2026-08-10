using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Tests;

public sealed class SchemaBoundsGateTests
{
    [Fact]
    public async Task Data_plane_returns_503_when_gate_not_ready()
    {
        await using var factory = new NotReadyGateFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            "/v1/changes/watermarks",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(SchemaBoundsAccessHost.NotReadyReason, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_plane_returns_503_when_schema_incompatible()
    {
        await using var factory = new GatedApiFactory(
            new ApiHealthSnapshot(Ok: false, Database: "ok", Schema: "incompatible", SchemaVersion: "1.0.0"));
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            "/v1/changes/watermarks",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("schema_bounds:incompatible", body, StringComparison.Ordinal);
        Assert.Contains("1.0.0", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_plane_returns_503_when_database_unreachable()
    {
        await using var factory = new RealHealthApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            "/v1/changes/watermarks",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("schema_bounds:db_unavailable", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Control_plane_stays_open_while_data_plane_blocked()
    {
        await using var factory = new GatedApiFactory(
            new ApiHealthSnapshot(Ok: false, Database: "unavailable", Schema: "skipped", SchemaVersion: null));
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var ping = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);
        using var info = await client.GetAsync("/v1/system/info", TestContext.Current.CancellationToken);
        using var watermarks = await client.GetAsync(
            "/v1/changes/watermarks",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, watermarks.StatusCode);
    }

    [Fact]
    public async Task Data_plane_opens_after_compatible_probe()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var guard = factory.Services.GetRequiredService<IDbAccessGuard>();
        Assert.False(guard.IsBlocked);

        using var response = await client.GetAsync(
            "/v1/changes/watermarks",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-version", "1.2.25")]
    [InlineData("1.2.25", "01.2.25")]
    [InlineData("1.2.25-beta", "1.2.25")]
    [InlineData("1.2.26", "1.2.25")]
    public void Host_startup_rejects_invalid_schema_bounds(string min, string max)
    {
        using var factory = new InvalidSchemaBoundsFactory(min, max);
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("SchemaBounds", Flatten(ex), StringComparison.Ordinal);
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            parts.Add(cur.Message);
        }

        return string.Join(" | ", parts);
    }

    /// <summary>去掉 SchemaBoundsAccessHost，保留启动时的 not_ready 阻断</summary>
    private sealed class NotReadyGateFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(ApiFactory.BuildAuthConfig());
            });
            builder.ConfigureTestServices(services =>
            {
                foreach (var d in services
                             .Where(static x =>
                                 x.ServiceType == typeof(IHostedService)
                                 && x.ImplementationType == typeof(SchemaBoundsAccessHost))
                             .ToList())
                {
                    services.Remove(d);
                }
            });
        }
    }

    private sealed class GatedApiFactory(ApiHealthSnapshot snapshot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(ApiFactory.BuildAuthConfig());
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IApiHealth>(new FixedApiHealth(snapshot));
            });
        }
    }

    private sealed class InvalidSchemaBoundsFactory(string min, string max) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var overrides = ApiFactory.BuildAuthConfig();
                overrides["SchemaBounds:MinDbSchema"] = min;
                overrides["SchemaBounds:MaxDbSchema"] = max;
                config.AddInMemoryCollection(overrides);
            });
        }
    }

    private sealed class FixedApiHealth(ApiHealthSnapshot snapshot) : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }
}
