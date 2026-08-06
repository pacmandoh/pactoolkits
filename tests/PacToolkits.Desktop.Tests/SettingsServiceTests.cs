using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void AppliedDb_returns_config_service_current()
    {
        var service = CreateService(new DbAccessGuard(), schemaVersion: "1.2.22");

        Assert.Equal("current-host", service.AppliedDb.Host);
        Assert.Equal("current-db", service.AppliedDb.Database);
    }

    [Fact]
    public async Task Validate_connection_reports_schema_below_minimum_without_migrating()
    {
        var service = CreateService(new DbAccessGuard(), schemaVersion: "1.2.20");

        var result = await service.ValidateDbConnectionAsync(
            new PgOptions(),
            Context("1.2.21", "1.2.22"),
            CancellationToken.None);

        Assert.True(result.ConnectionOk);
        Assert.False(result.SchemaCompatible);
        Assert.Contains("不兼容", result.IncompatibleMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_candidate_connection_does_not_block_current_database()
    {
        var guard = new DbAccessGuard();
        var service = CreateService(guard, schemaVersion: "1.2.23");

        var result = await service.ValidateDbConnectionAsync(
            new PgOptions(),
            Context("1.2.20", "1.2.22"),
            CancellationToken.None);

        Assert.True(result.ConnectionOk);
        Assert.False(result.SchemaCompatible);
        Assert.False(guard.IsBlocked);
        Assert.Contains("数据库版本高于当前程序支持范围", result.IncompatibleMessage ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_current_connection_status_blocks_database_when_schema_is_above_maximum()
    {
        var guard = new DbAccessGuard();
        var service = CreateService(guard, schemaVersion: "1.2.23");

        var snapshot = await service.GetSchemaStatusAsync(
            Context("1.2.20", "1.2.22"),
            CancellationToken.None);

        Assert.Equal(DbSchemaCompatibility.AboveMaximum, snapshot.Compatibility);
        Assert.True(guard.IsBlocked);
    }

    [Fact]
    public async Task Validate_connection_uses_explicit_options_not_current_config()
    {
        var schemaService = new TrackingSchemaVersionService();
        var currentConfig = new FakeDbConfigService();
        var explicitOptions = new PgOptions { Host = "explicit-host", Database = "explicit-db" };
        var service = new SettingsService(
            currentConfig,
            new FakeConnectionTester(),
            schemaService,
            new FakeClientIdReadRepo(),
            new DbAccessGuard());

        await service.ValidateDbConnectionAsync(
            explicitOptions,
            Context("1.2.20", "1.2.22"),
            CancellationToken.None);

        Assert.Equal(explicitOptions.Host, schemaService.LastOptions?.Host);
        Assert.Equal(explicitOptions.Database, schemaService.LastOptions?.Database);
        Assert.NotEqual(explicitOptions.Host, currentConfig.Current.Host);
    }

    [Fact]
    public async Task Metadata_missing_status_is_incompatible_and_read_only()
    {
        var service = CreateService(
            new DbAccessGuard(),
            schemaReadResult: new DbSchemaVersionRead(
                false,
                null,
                "schema_version 表不存在",
                IsMetadataMissing: true));

        var snapshot = await service.GetSchemaStatusAsync(
            Context("1.2.20", "1.2.22"),
            new PgOptions(),
            CancellationToken.None);

        Assert.Equal(DbSchemaCompatibility.MetadataMissing, snapshot.Compatibility);
        Assert.False(snapshot.Satisfied);
    }

    private static DbSchemaVersionContext Context(string minimum, string maximum)
        => new(
            DesktopMinDbSchema: minimum,
            DesktopMaxDbSchema: maximum,
            TargetDbSchemaVersion: maximum);

    private static SettingsService CreateService(
        DbAccessGuard guard,
        string schemaVersion = "1.2.20",
        DbSchemaVersionRead? schemaReadResult = null)
        => new(
            new FakeDbConfigService(),
            new FakeConnectionTester(),
            new FakeSchemaVersionService(schemaReadResult ?? new DbSchemaVersionRead(true, schemaVersion, null)),
            new FakeClientIdReadRepo(),
            guard);

    private sealed class FakeDbConfigService : IDbConfigService
    {
        public PgOptions Current { get; } = new() { Host = "current-host", Database = "current-db" };
        public string ConfigPath => "/tmp/pactoolkits-test.config.json";
        public event EventHandler? Applied;

        public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(true);

        public Task ApplyAsync(PgOptions opt, CancellationToken ct = default)
        {
            Applied?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnectionTester : IDbConnectionTester
    {
        public Task<DbTestResult> TestAsync(PgOptions opt, CancellationToken ct = default)
            => Task.FromResult(new DbTestResult(true, "ok"));
    }

    private sealed class FakeSchemaVersionService(DbSchemaVersionRead result) : IDbSchemaVersionService
    {
        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(result);

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(result);
    }

    private sealed class TrackingSchemaVersionService : IDbSchemaVersionService
    {
        public PgOptions? LastOptions { get; private set; }

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionRead(true, "1.2.19", null));

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
        {
            LastOptions = options;
            return Task.FromResult(new DbSchemaVersionRead(true, "1.2.19", null));
        }
    }

    private sealed class FakeClientIdReadRepo : IClientIdReadRepo
    {
        public Task<HashSet<string>> GetDistinctClientIdsAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(new HashSet<string>());
    }
}
