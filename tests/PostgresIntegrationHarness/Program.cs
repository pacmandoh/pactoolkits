using System.Data;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;
using PacToolkits.Infrastructure.Database;

static PgOptions LoadOptions(string? database = null)
    => new()
    {
        Host = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1",
        Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var port) ? port : 5432,
        Database = database ?? Environment.GetEnvironmentVariable("PGDATABASE") ?? "codepool_dev",
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
        new DbSchemaVersionService(config, logger),
        new DbSchemaMigrationService(config, logger),
        new ClientIdReadRepo(logger, guard),
        guard,
        new DbMigrationPolicyService(new DbEnvSettingsService(config, logger)),
        new DbEnvSettingsService(config, logger));
}

static PgDb CreatePgDb(PgOptions options, DbAccessGuard guard, NullLogger logger)
{
    var factory = new PgDataSourceFactory();
    factory.Rebuild(options);
    return new PgDb(factory, logger, guard);
}

var options = LoadOptions();
var betaDatabase = Environment.GetEnvironmentVariable("PG_ITEST_BETA_DATABASE");
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
    if (!read.Ok || string.IsNullOrWhiteSpace(read.Value))
        Fail("schema_version_read", read.Reason ?? "empty");
    else
        Pass($"schema_version_read={read.Value}");

    var env = new DbEnvSettingsService(new FixedDbConfig(options), logger);
    var productionEnv = await env.TryReadAsync(options, CancellationToken.None);
    if (!string.Equals(productionEnv.Environment, "production", StringComparison.OrdinalIgnoreCase) || productionEnv.AllowBetaMigrations)
        Fail("production_env_defaults", $"{productionEnv.Environment}/{productionEnv.AllowBetaMigrations}");
    else
        Pass("production_env_defaults=production/false");

    var guard = new DbAccessGuard();
    var clients = new ClientIdReadRepo(logger, guard);
    var machines = await clients.GetDistinctClientIdsAsync(options, CancellationToken.None);
    Pass($"client_alias_read count={machines.Count}");

    var service = CreateService(options, guard);
    var stableContext = new DbSchemaVersionContext(
        "1.2.20", "1.2.23", "1.2.20", "1.2.23", "1.2.23", "stable", DbMigrationPolicies.StableOnly);
    var snapshot = await service.ReadSchemaStatusAsync(stableContext, options, CancellationToken.None);
    if (!snapshot.SchemaOk || snapshot.Compatibility != DbSchemaCompatibility.Compatible)
        Fail("stable_schema_status", $"{snapshot.Compatibility} {snapshot.Reason}");
    else
        Pass("stable_schema_status=compatible");

    var belowMinContext = new DbSchemaVersionContext(
        "1.2.24", "1.2.25", "1.2.24", "1.2.25", "1.2.25",
        "beta", DbMigrationPolicies.StableOnly);
    var belowSnapshot = await service.ReadSchemaStatusAsync(belowMinContext, options, CancellationToken.None);
    if (belowSnapshot.ManualMigrationPolicy.Decision != DbMigrationDecision.ReadOnlyRequired
        || !belowSnapshot.ManualMigrationPolicy.Reason.Contains("Beta 应用禁止迁移", StringComparison.Ordinal))
        Fail("beta_policy_block", belowSnapshot.ManualMigrationPolicy.Reason);
    else
        Pass("beta_policy_block");

    var before = await schema.TryReadSchemaVersionAsync(options, CancellationToken.None);
    var plan = await service.GetSchemaMigrationPlanAsync(stableContext, options, CancellationToken.None);
    var after = await schema.TryReadSchemaVersionAsync(options, CancellationToken.None);
    if (!string.Equals(before.Value, after.Value, StringComparison.Ordinal))
        Fail("migration_plan_no_mutation", $"{before.Value} -> {after.Value}");
    else
        Pass($"migration_plan_no_mutation pending={plan.PendingCount}");

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
    var probeCountBefore = await CountEnvSettingAsync(options, probeKey);
    try
    {
        await pgDb.WithTransaction(async (conn, tx, ct) =>
        {
            if (conn is not NpgsqlConnection npgsqlConn)
                throw new InvalidOperationException("expected NpgsqlConnection");
            await using var cmd = new NpgsqlCommand(
                """
                insert into public.app_environment_settings(environment, setting_key, setting_value)
                values (@env, @k, to_jsonb(@v::text))
                on conflict (environment, setting_key) do update
                set setting_value = excluded.setting_value,
                    updated_at = now()
                """,
                npgsqlConn,
                (NpgsqlTransaction)tx);
            cmd.Parameters.AddWithValue("env", "integration-test");
            cmd.Parameters.AddWithValue("k", probeKey);
            cmd.Parameters.AddWithValue("v", "probe");
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException("rollback-probe");
        }, IsolationLevel.ReadCommitted, CancellationToken.None);
        Fail("guard_rollback_write", "expected rollback-probe exception");
    }
    catch (InvalidOperationException ex) when (ex.Message == "rollback-probe")
    {
        var probeCountAfter = await CountEnvSettingAsync(options, probeKey);
        if (probeCountAfter != probeCountBefore)
            Fail("guard_rollback_write", $"probe persisted: before={probeCountBefore}, after={probeCountAfter}");
        else
            Pass("guard_rollback_write");
    }

    if (!string.IsNullOrWhiteSpace(betaDatabase))
    {
        var betaOptions = LoadOptions(betaDatabase);
        var betaEnv = await new DbEnvSettingsService(new FixedDbConfig(betaOptions), logger)
            .TryReadAsync(betaOptions, CancellationToken.None);
        if (!betaEnv.IsIsolated || !betaEnv.AllowBetaMigrations)
            Fail("beta_env_markers", $"{betaEnv.Environment}/{betaEnv.AllowBetaMigrations}");
        else
            Pass("beta_env_markers=isolated/true");

        var betaService = CreateService(betaOptions, new DbAccessGuard());
        var betaSnapshot = await betaService.ReadSchemaStatusAsync(
            new DbSchemaVersionContext(
                "1.2.20", "1.2.23", "1.2.20", "1.2.23", "1.2.23",
                "beta", DbMigrationPolicies.IsolatedBeta),
            betaOptions,
            CancellationToken.None);
        if (betaSnapshot.ManualMigrationPolicy.Decision != DbMigrationDecision.RequiresConfirmation
            && betaSnapshot.ManualMigrationPolicy.Decision != DbMigrationDecision.Allowed)
            Fail("isolated_beta_policy", betaSnapshot.ManualMigrationPolicy.Reason);
        else
            Pass($"isolated_beta_policy={betaSnapshot.ManualMigrationPolicy.Decision}");
    }
    else
    {
        Console.WriteLine("[app-itest][skip] beta database checks (PG_ITEST_BETA_DATABASE not set)");
    }
}
catch (Exception ex)
{
    Fail("unhandled", ex.ToString());
}

return failures == 0 ? 0 : 1;

static async Task<int> CountEnvSettingAsync(PgOptions options, string key)
{
    await using var conn = await OpenConnectionAsync(options);
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        select count(*)
        from public.app_environment_settings
        where environment = @env and setting_key = @k
        """;
    cmd.Parameters.AddWithValue("env", "integration-test");
    cmd.Parameters.AddWithValue("k", key);
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

sealed class FixedDbConfig(PgOptions current) : IDbConfigService
{
    public PgOptions Current { get; } = current;
    public string ConfigPath => "/tmp/pactoolkits-itest.config.json";
#pragma warning disable CS0067
    public event EventHandler? Applied;
#pragma warning restore CS0067
    public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct) => Task.FromResult(true);
    public Task ApplyAsync(PgOptions opt, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class NullLogger : IAppLogger
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
