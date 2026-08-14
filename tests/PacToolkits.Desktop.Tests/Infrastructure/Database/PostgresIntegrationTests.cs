using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Core;
using PacToolkits.Infrastructure.Database;
using PacToolkits.Tests.Shared;

namespace PacToolkits.Desktop.Tests;

[Trait("Category", "PostgresIntegration")]
public sealed class PostgresIntegrationTests
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("PG_ITEST"), "1", StringComparison.Ordinal);

    private static void RequireEnabled()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("PG_ITEST is not set. Run tests/scripts/run-postgres-integration.sh.");
        }
    }

    [Fact]
    public async Task Real_database_reads_compatible_schema()
    {
        RequireEnabled();

        var options = LoadPgOptions();
        var logger = new NullInfraLogger();
        var schema = new DbSchemaVersionService(new FixedDbConfig(options), logger);
        var read = await schema.TryReadSchemaVersionAsync(options, CancellationToken.None);

        Assert.True(read.Ok, read.Reason);
        Assert.False(string.IsNullOrWhiteSpace(read.Value));

        var bounds = ManifestDbSchema.CompatibleBounds();
        var match = new DbSchemaGate(schema).Match(read, bounds.Min, bounds.Max);
        Assert.Equal(DbSchemaCompatibility.Compatible, match.Status);
        Assert.True(match.IsCompatible);
    }

    private static PgOptions LoadPgOptions(string? database = null)
        => new()
        {
            Host = Environment.GetEnvironmentVariable("PGHOST") ?? "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var port) ? port : 5432,
            Database = database ?? Environment.GetEnvironmentVariable("PGDATABASE") ?? "postgres",
            Username = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres",
            Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? string.Empty,
        };

    private sealed class FixedDbConfig(PgOptions current) : IDbConfigService
    {
        public PgOptions Current { get; } = current;

        public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class NullInfraLogger : IAppLogger
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
}
