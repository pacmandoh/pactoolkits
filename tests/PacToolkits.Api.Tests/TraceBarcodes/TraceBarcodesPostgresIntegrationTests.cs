using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

/// <summary>
/// 使用测试 PostgreSQL 验证 TraceBarcode 预留、审计幂等与 CommandId 回放
///
/// 需要 <c>PG_ITEST=1</c> 与 PostgreSQL 连接环境变量
/// </summary>
[Trait("Category", "PostgresIntegration")]
public sealed class TraceBarcodesPostgresIntegrationTests
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("PG_ITEST"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task Pick_reserves_available_trace_codes_and_respects_recent_audit_exclusion()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        var drugId = $"pg-pick-{Guid.NewGuid():N}"[..20];
        var spec = "itest";
        var freshCode = $"PGPICK-{Guid.NewGuid():N}";
        var auditedCode = $"PGAUD-{Guid.NewGuid():N}";

        await using var connection = await OpenConnectionAsync();
        await SeedDrugAndTraceAsync(connection, drugId, spec, freshCode, auditedCode);

        await using var factory = new PostgresTraceBarcodeApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var auditBatchId = Guid.NewGuid();
        using (var audit = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
        {
            Content = new StringContent(
                $$"""
                {
                  "batchId": "{{auditBatchId:D}}",
                  "action": "preview",
                  "operatorName": "pg-itest@host",
                  "items": [
                    { "traceCode": "{{auditedCode}}", "drugId": "{{drugId}}", "spec": "{{spec}}", "fileName": null, "success": true, "error": null }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        })
        {
            audit.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
            using var auditResponse = await client.SendAsync(audit, TestContext.Current.CancellationToken);
            auditResponse.EnsureSuccessStatusCode();
        }

        var pickBatchId = Guid.NewGuid();
        using var pick = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/pick")
        {
            Content = new StringContent(
                $$"""
                {
                  "batchId": "{{pickBatchId:D}}",
                  "operatorName": "pg-itest@host",
                  "items": [{ "drugId": "{{drugId}}", "spec": "{{spec}}", "count": 2 }],
                  "excludeRecentDays": 7,
                  "excludeTraceCodes": []
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        pick.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var pickResponse = await client.SendAsync(pick, TestContext.Current.CancellationToken);
        var pickBody = await pickResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        pickResponse.EnsureSuccessStatusCode();

        using var pickDoc = JsonDocument.Parse(pickBody);
        var codes = pickDoc.RootElement
            .GetProperty("groups")[0]
            .GetProperty("codes")
            .EnumerateArray()
            .Select(static e => e.GetProperty("traceCode").GetString())
            .Where(static c => c is not null)
            .Cast<string>()
            .ToArray();

        Assert.Single(codes);
        Assert.Equal(freshCode, codes[0]);
        Assert.DoesNotContain(auditedCode, codes);

        await using var reservedCmd = new NpgsqlCommand(
            "select count(*)::int from trace_barcode_audit_log where batch_id = @batch_id and action = 'preview'",
            connection);
        reservedCmd.Parameters.AddWithValue("batch_id", pickBatchId);
        var reserved = (int)(await reservedCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0);
        Assert.Equal(1, reserved);
    }

    [Fact]
    public async Task Audit_replays_command_id_against_real_database()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresTraceBarcodeApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var commandId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var json = $$"""
            {
              "batchId": "{{batchId:D}}",
              "action": "preview",
              "operatorName": "pg-itest@host",
              "items": [
                { "traceCode": "PGIT-{{batchId:N}}", "drugId": "d1", "spec": "s1", "fileName": null, "success": true, "error": null }
              ]
            }
            """;

        async Task<(HttpStatusCode Status, string Body)> SendOnce()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, commandId.ToString("D"));
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        var first = await SendOnce();
        var second = await SendOnce();

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body, second.Body);
        using var doc = JsonDocument.Parse(first.Body);
        Assert.Equal(1, doc.RootElement.GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task Audit_duplicate_batch_action_code_is_idempotent()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresTraceBarcodeApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var batchId = Guid.NewGuid();
        var traceCode = $"PGDUP-{batchId:N}";
        var json = $$"""
            {
              "batchId": "{{batchId:D}}",
              "action": "export",
              "operatorName": "pg-itest@host",
              "items": [
                { "traceCode": "{{traceCode}}", "drugId": "d1", "spec": "s1", "fileName": "a.png", "success": true, "error": null }
              ]
            }
            """;

        async Task<int> InsertOnce()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return doc.RootElement.GetProperty("inserted").GetInt32();
        }

        Assert.Equal(1, await InsertOnce());
        Assert.Equal(0, await InsertOnce());
    }

    [Fact]
    public async Task Clear_audit_log_deletes_rows()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresTraceBarcodeApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var batchId = Guid.NewGuid();
        using (var seed = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit")
        {
            Content = new StringContent(
                $$"""
                {
                  "batchId": "{{batchId:D}}",
                  "action": "preview",
                  "operatorName": "pg-itest@host",
                  "items": [
                    { "traceCode": "PGCLR-{{batchId:N}}", "drugId": null, "spec": null, "fileName": null, "success": true, "error": null }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        })
        {
            seed.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
            using var seedResponse = await client.SendAsync(seed, TestContext.Current.CancellationToken);
            seedResponse.EnsureSuccessStatusCode();
        }

        using var clear = new HttpRequestMessage(HttpMethod.Post, "/v1/trace-barcodes/audit-log/clear")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        clear.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        using var clearResponse = await client.SendAsync(clear, TestContext.Current.CancellationToken);
        clearResponse.EnsureSuccessStatusCode();
        using var clearDoc = JsonDocument.Parse(
            await clearResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(clearDoc.RootElement.GetProperty("deleted").GetInt32() >= 1);

        await using var connection = await OpenConnectionAsync();
        await using var countCmd = new NpgsqlCommand(
            "select count(*)::int from trace_barcode_audit_log where batch_id = @batch_id",
            connection);
        countCmd.Parameters.AddWithValue("batch_id", batchId);
        var remaining = (int)(await countCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0);
        Assert.Equal(0, remaining);
    }

    private static async Task SeedDrugAndTraceAsync(
        NpgsqlConnection connection,
        string drugId,
        string spec,
        params string[] traceCodes)
    {
        await using (var drugCmd = new NpgsqlCommand(
            """
            insert into drug_index(drug_id, spec, qty)
            values (@drug_id, @spec, 1)
            on conflict (drug_id, spec) do nothing
            """,
            connection))
        {
            drugCmd.Parameters.AddWithValue("drug_id", drugId);
            drugCmd.Parameters.AddWithValue("spec", spec);
            await drugCmd.ExecuteNonQueryAsync();
        }

        foreach (var traceCode in traceCodes)
        {
            await using var poolCmd = new NpgsqlCommand(
                """
                insert into trace_pool(drug_id, spec, qty, trace_code, remain, status)
                values (@drug_id, @spec, 1, @trace_code, 1, 1)
                on conflict (trace_code) do nothing
                """,
                connection);
            poolCmd.Parameters.AddWithValue("drug_id", drugId);
            poolCmd.Parameters.AddWithValue("spec", spec);
            poolCmd.Parameters.AddWithValue("trace_code", traceCode);
            await poolCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("PGDATABASE") ?? "pactoolkits",
            Username = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres",
            Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private sealed class PostgresTraceBarcodeApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = ApiFactory.BuildAuthConfig();
                values["Postgres:Host"] = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1";
                values["Postgres:Port"] = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
                values["Postgres:Database"] = Environment.GetEnvironmentVariable("PGDATABASE") ?? "pactoolkits";
                values["Postgres:Username"] = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres";
                values["Postgres:Password"] = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty;
                values["Changes:ListenEnabled"] = "false";
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(ICommandDedup));
                services.AddSingleton<ICommandDedup, PgCommandDedup>();
                services.AddSingleton<IApiHealth>(_ => new AlwaysOkApiHealth());
            });
        }
    }

    private sealed class AlwaysOkApiHealth : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiHealthSnapshot(Ok: true, Database: "ok", Schema: "ok", SchemaVersion: "1.2.28"));
    }
}
