using System.Data;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Core;
using PacToolkits.Infrastructure.Database;
using PacToolkits.Tests.Shared;

static PgOptions LoadOptions(string? database = null)
    => new()
    {
        Host = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1",
        Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var port) ? port : 5432,
        Database = database ?? Environment.GetEnvironmentVariable("PGDATABASE") ?? "postgres",
        Username = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres",
        Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty,
    };

static SettingsService CreateService(PgOptions options, DbAccessGuard guard)
{
    var config = new FixedDbConfig(options);
    var logger = new NullLogger();
    return new SettingsService(
        config,
        new DbConnectionTester(logger),
        new DbSchemaGate(new DbSchemaVersionService(config, logger)),
        new ClientIdReadRepo(logger, guard),
        guard);
}

static PgDb CreatePgDb(PgOptions options, DbAccessGuard guard, NullLogger logger)
{
    var factory = new PgDataSourceFactory();
    factory.Rebuild(options);
    return new PgDb(factory, logger, guard);
}

var options = LoadOptions();
var failures = 0;

void Pass(string name) => Console.WriteLine($"[app-itest][pass] {name}");
void Fail(string name, string detail)
{
    failures++;
    Console.Error.WriteLine($"[app-itest][fail] {name}: {detail}");
}

try
{
    var logger = new NullLogger();
    var schema = new DbSchemaVersionService(new FixedDbConfig(options), logger);
    var read = await schema.TryReadSchemaVersionAsync(options, CancellationToken.None);
    string? liveSchema = null;
    if (!read.Ok || string.IsNullOrWhiteSpace(read.Value))
    {
        Fail("schema_version_read", read.Reason ?? "empty");
    }
    else
    {
        liveSchema = read.Value;
        Pass($"schema_version_read={liveSchema}");
    }

    var guard = new DbAccessGuard();
    var clients = new ClientIdReadRepo(logger, guard);
    var machines = await clients.GetDistinctClientIdsAsync(options, CancellationToken.None);
    Pass($"client_alias_read count={machines.Count}");

    var service = CreateService(options, guard);
    var compatibleContext = ManifestDbSchema.CompatibleContext();
    var snapshot = await service.GetSchemaStatusAsync(compatibleContext, options, CancellationToken.None);
    if (!snapshot.SchemaOk || snapshot.Compatibility != DbSchemaCompatibility.Compatible)
    {
        Fail("schema_status", $"{snapshot.Compatibility} {snapshot.Reason}");
    }
    else
    {
        Pass("schema_status=compatible");
    }

    // BelowMinimum：相对现场库推算，避免手写下一版号
    if (liveSchema is not null)
    {
        var belowMinContext = ManifestDbSchema.BelowMinimumContext(liveSchema);
        var belowSnapshot = await service.GetSchemaStatusAsync(belowMinContext, options, CancellationToken.None);
        if (belowSnapshot.Compatibility != DbSchemaCompatibility.BelowMinimum || belowSnapshot.Satisfied)
        {
            Fail("below_minimum_block", belowSnapshot.IncompatibleMessage ?? belowSnapshot.Compatibility.ToString());
        }
        else
        {
            Pass("below_minimum_block");
        }
    }

    var pgDb = CreatePgDb(options, guard, logger);
    guard.Block("integration-test block");
    try
    {
        await pgDb.WithConnection(async (_, _) => await Task.FromResult(0), CancellationToken.None);
        Fail("guard_blocks_write", "expected InvalidOperationException");
    }
    catch (InvalidOperationException)
    {
        Pass("guard_blocks_write");
    }

    guard.Clear();
    var probeKey = $"ITest.Harness.{Guid.NewGuid():N}";
    var probeCountBefore = await CountWatermarkAsync(options, probeKey);
    try
    {
        await pgDb.WithTransaction(async (conn, tx, ct) =>
        {
            if (conn is not NpgsqlConnection npgsqlConn)
            {
                throw new InvalidOperationException("expected NpgsqlConnection");
            }

            await using var cmd = new NpgsqlCommand(
                """
                insert into public.app_change_watermark(topic, version)
                values (@k, 1)
                on conflict (topic) do update
                set version = app_change_watermark.version + 1,
                    updated_at = now()
                """,
                npgsqlConn,
                (NpgsqlTransaction)tx);
            cmd.Parameters.AddWithValue("k", probeKey);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException("rollback-probe");
        }, IsolationLevel.ReadCommitted, CancellationToken.None);
        Fail("guard_rollback_write", "expected rollback-probe exception");
    }
    catch (InvalidOperationException ex) when (ex.Message == "rollback-probe")
    {
        var probeCountAfter = await CountWatermarkAsync(options, probeKey);
        if (probeCountAfter != probeCountBefore)
        {
            Fail("guard_rollback_write", $"probe persisted: before={probeCountBefore}, after={probeCountAfter}");
        }
        else
        {
            Pass("guard_rollback_write");
        }
    }
}
catch (Exception ex)
{
    Fail("unhandled", ex.ToString());
}

return failures == 0 ? 0 : 1;

static async Task<int> CountWatermarkAsync(PgOptions options, string topic)
{
    await using var conn = await OpenConnectionAsync(options);
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        select count(*)
        from public.app_change_watermark
        where topic = @k
        """;
    cmd.Parameters.AddWithValue("k", topic);
    var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
    return Convert.ToInt32(result);
}

static async Task<NpgsqlConnection> OpenConnectionAsync(PgOptions options)
{
    var csb = new NpgsqlConnectionStringBuilder
    {
        Host = options.Host,
        Port = options.Port,
        Database = options.Database,
        Username = options.Username,
        Password = options.Password,
        SearchPath = "public",
        Timeout = options.ConnectTimeoutSeconds,
    };
    var conn = new NpgsqlConnection(csb.ConnectionString);
    await conn.OpenAsync().ConfigureAwait(false);
    return conn;
}

file sealed class FixedDbConfig(PgOptions current) : IDbConfigService
{
    public PgOptions Current { get; } = current;
    public string ConfigPath => "/tmp/pactoolkits-itest.config.json";
#pragma warning disable CS0067
    public event EventHandler? Applied;
#pragma warning restore CS0067
    public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct) => Task.FromResult(true);
    public Task ApplyAsync(PgOptions opt, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class NullLogger : IAppLogger
{
    public string LogDirectory => "/tmp";
    public string CurrentLogPath => "/tmp/pactoolkits-itest.log";
    public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
    public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
    public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
    public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
    public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
    public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default) => Task.FromResult(string.Empty);
}
