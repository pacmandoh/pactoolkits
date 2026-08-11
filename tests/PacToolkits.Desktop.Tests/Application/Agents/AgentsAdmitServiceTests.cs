using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsAdmitServiceTests
{
    [Fact]
    public async Task Admit_skips_schema_when_module_has_no_db_range()
    {
        var sut = new AgentsAdmitService(new DbSchemaGate(new FixedSchema("1.2.25")));
        var result = await sut.AdmitAsync(
            new AgentsModuleDbBound("LocalOnly", null, null),
            databaseConnected: false,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.None, result.DenyKind);
    }

    [Fact]
    public async Task Admit_disconnect_when_module_requires_db()
    {
        var sut = new AgentsAdmitService(new DbSchemaGate(new FixedSchema("1.2.25")));
        var result = await sut.AdmitAsync(
            new AgentsModuleDbBound("Injector", "1.2.20", "1.2.25"),
            databaseConnected: false,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.Disconnected, result.DenyKind);
    }

    [Fact]
    public async Task Admit_rejects_schema_below_min()
    {
        var sut = new AgentsAdmitService(new DbSchemaGate(new FixedSchema("1.2.19")));
        var result = await sut.AdmitAsync(
            new AgentsModuleDbBound("Injector", "1.2.20", "1.2.25"),
            databaseConnected: true,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.SchemaOutOfRange, result.DenyKind);
        Assert.Contains("过低", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("below min", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmitMany_reads_schema_once_and_mixes_results()
    {
        var schema = new CountingSchema("1.2.22");
        var sut = new AgentsAdmitService(new DbSchemaGate(schema));
        var results = await sut.AdmitManyAsync(
            [
                new AgentsModuleDbBound("A", "1.2.20", "1.2.25"),
                new AgentsModuleDbBound("B", null, null),
                new AgentsModuleDbBound("C", "1.2.23", "1.2.25"),
            ],
            databaseConnected: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Ok);
        Assert.True(results[1].Ok);
        Assert.False(results[2].Ok);
        Assert.Equal(1, schema.ReadCount);
    }

    [Fact]
    public async Task Admit_rejects_when_schema_unread()
    {
        var sut = new AgentsAdmitService(new DbSchemaGate(new FailedSchema()));
        var result = await sut.AdmitAsync(
            new AgentsModuleDbBound("Injector", "1.2.20", "1.2.25"),
            databaseConnected: true,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(AgentsAdmitDenyKind.SchemaUnread, result.DenyKind);
    }

    private sealed class FixedSchema(string version) : IDbSchemaVersionService
    {
        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionRead(true, version, null));

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(
            PgOptions options,
            CancellationToken ct)
            => TryReadSchemaVersionAsync(ct);
    }

    private sealed class CountingSchema(string version) : IDbSchemaVersionService
    {
        public int ReadCount { get; private set; }

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(new DbSchemaVersionRead(true, version, null));
        }

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(
            PgOptions options,
            CancellationToken ct)
            => TryReadSchemaVersionAsync(ct);
    }

    private sealed class FailedSchema : IDbSchemaVersionService
    {
        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionRead(false, null, "schema table missing"));

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(
            PgOptions options,
            CancellationToken ct)
            => TryReadSchemaVersionAsync(ct);
    }
}
