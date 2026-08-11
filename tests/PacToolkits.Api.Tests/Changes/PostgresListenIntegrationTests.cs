using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PacToolkits.Api.Changes;
using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Tests;

/// <summary>
/// 真实 PostgreSQL：LISTEN 唤醒 SSE change，再 GET watermarks
///
/// 默认 Skip；设 <c>PG_ITEST=1</c> 与 Pg 连接环境变量后执行
/// </summary>
[Trait("Category", "PostgresIntegration")]
public sealed class PostgresListenIntegrationTests
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("PG_ITEST"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task Listen_notify_reaches_sse_and_watermarks()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // LISTEN 建连需要一点时间
        await Task.Delay(500, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/changes/stream");
        using var streamResponse = await client.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
        streamResponse.EnsureSuccessStatusCode();

        await using var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new StringBuilder();

        while (buffer.ToString().IndexOf("event: ready", StringComparison.Ordinal) < 0)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            buffer.AppendLine(line);
        }

        await NotifyAsync("inventory", cts.Token);

        while (buffer.ToString().IndexOf("inventory", StringComparison.Ordinal) < 0)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            buffer.AppendLine(line);
        }

        Assert.Contains("event: change", buffer.ToString(), StringComparison.Ordinal);

        using var watermarks = await client.GetAsync("/v1/changes/watermarks", cts.Token);
        var body = await watermarks.Content.ReadAsStringAsync(cts.Token);
        Assert.True(watermarks.IsSuccessStatusCode, body);
        Assert.Contains("topic", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task NotifyAsync(string topic, CancellationToken ct)
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("PGDATABASE") ?? "postgres",
            Username = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres",
            Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty,
            Timeout = 5,
        };

        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select pg_notify(@ch, @payload);";
        cmd.Parameters.AddWithValue("ch", PostgresNotifyListener.NotifyChannel);
        cmd.Parameters.AddWithValue("payload", topic);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed class PostgresApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var overrides = ApiFactory.BuildAuthConfig();
                overrides["Changes:ListenEnabled"] = "true";
                overrides["Postgres:Host"] = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1";
                overrides["Postgres:Port"] =
                    Environment.GetEnvironmentVariable("PGPORT")
                    ?? "5432";
                overrides["Postgres:Database"] = Environment.GetEnvironmentVariable("PGDATABASE") ?? "postgres";
                overrides["Postgres:Username"] = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres";
                overrides["Postgres:Password"] = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty;
                overrides["Postgres:ConnectTimeoutSeconds"] = "5";
                config.AddInMemoryCollection(overrides);
            });
            builder.ConfigureTestServices(services =>
            {
                // 健康门放行；LISTEN / watermarks 仍走真实 Pg
                services.AddSingleton<IApiHealth>(_ => new AlwaysOkApiHealth());
            });
        }
    }

    private sealed class AlwaysOkApiHealth : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiHealthSnapshot(Ok: true, Database: "ok", Schema: "ok", SchemaVersion: "1.2.26"));
    }
}
