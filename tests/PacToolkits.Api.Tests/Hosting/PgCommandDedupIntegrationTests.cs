using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Api.Tests;

/// <summary>
/// 真实 PostgreSQL 上的持久化 dedup；Claim、Complete 与业务同事务
///
/// 默认 Skip；设 <c>PG_ITEST=1</c> 与 Pg 连接环境变量后执行
/// </summary>
[Trait("Category", "PostgresIntegration")]
public sealed class PgCommandDedupIntegrationTests
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("PG_ITEST"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task Claim_action_complete_same_transaction_replays()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresWriteApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDb>();
        var dedup = scope.ServiceProvider.GetRequiredService<ICommandDedup>();
        Assert.IsType<PgCommandDedup>(dedup);

        var commandId = Guid.NewGuid();
        var key = new CommandDedupKey(
            "pg-itest",
            "pg_dedup.itest",
            commandId,
            CommandDigest.Sha256Utf8("payload-a"));

        var write = await db.WithTransaction(
            async (_, _, token) =>
            {
                var claim = await dedup.ClaimAsync(key, token);
                Assert.Equal(CommandDedupClaim.Acquired, claim.Outcome);
                var entry = new CommandDedupEntry(200, "application/json", """{"ok":true}"""u8.ToArray());
                await dedup.CompleteAsync(key, entry, CancellationToken.None);
                return entry;
            },
            ct: TestContext.Current.CancellationToken);

        var replay = await dedup.ClaimAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(CommandDedupClaim.Completed, replay.Outcome);
        Assert.Equal(write.StatusCode, replay.Cached!.StatusCode);
    }

    [Fact]
    public async Task Failed_action_rolls_back_claim()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresWriteApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDb>();
        var dedup = scope.ServiceProvider.GetRequiredService<ICommandDedup>();

        var key = new CommandDedupKey(
            "pg-itest",
            "pg_dedup.itest_fail",
            Guid.NewGuid(),
            CommandDigest.Sha256Utf8("payload-b"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.WithTransaction(
                async (_, _, token) =>
                {
                    var claim = await dedup.ClaimAsync(key, token);
                    Assert.Equal(CommandDedupClaim.Acquired, claim.Outcome);
                    throw new InvalidOperationException("boom");
                },
                ct: TestContext.Current.CancellationToken));

        var again = await dedup.ClaimAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(CommandDedupClaim.Acquired, again.Outcome);
        await dedup.ReleaseAsync(key, CancellationToken.None);
    }

    [Fact]
    public async Task Business_sql_error_rolls_back_claim_and_keeps_root_exception()
    {
        Assert.SkipUnless(Enabled, "Set PG_ITEST=1 and Postgres env (see tests/scripts/run-postgres-integration.sh)");

        await using var factory = new PostgresWriteApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDb>();
        var dedup = scope.ServiceProvider.GetRequiredService<ICommandDedup>();

        var key = new CommandDedupKey(
            "pg-itest",
            "pg_dedup.itest_sql_fail",
            Guid.NewGuid(),
            CommandDigest.Sha256Utf8("payload-c"));

        // 同事务：Claim、业务写与语句错误；Pg 失败路径不 Release
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.WithTransaction(
                async (conn, _, token) =>
                {
                    var claim = await dedup.ClaimAsync(key, token);
                    Assert.Equal(CommandDedupClaim.Acquired, claim.Outcome);

                    await using (var marker = conn.CreateCommand(
                                     """
                                     create temporary table if not exists pg_dedup_itest_marker (id int);
                                     insert into pg_dedup_itest_marker (id) values (1);
                                     """,
                                     timeoutSeconds: 30))
                    {
                        await marker.ExecuteNonQueryAsync(token);
                    }

                    await using var bad = conn.CreateCommand(
                        "select 1 from pg_dedup_itest_no_such_table",
                        timeoutSeconds: 30);
                    await bad.ExecuteNonQueryAsync(token);
                    return 0;
                },
                ct: TestContext.Current.CancellationToken));

        // 42P01 undefined_table；事务已中止后再 Release 会盖成 25P02
        Assert.Equal("42P01", ex.SqlState);

        var again = await dedup.ClaimAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(CommandDedupClaim.Acquired, again.Outcome);
        await dedup.ReleaseAsync(key, CancellationToken.None);
    }

    private sealed class PostgresWriteApiFactory : WebApplicationFactory<Program>
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
                values["Postgres:Password"] = Environment.GetEnvironmentVariable("PGPASSWORD") ?? "postgres";
                values["Changes:ListenEnabled"] = "false";
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                // 保留 PgCommandDedup，不要换成 Memory
                services.RemoveAll(typeof(ICommandDedup));
                services.AddSingleton<ICommandDedup, PgCommandDedup>();
            });
        }
    }
}
