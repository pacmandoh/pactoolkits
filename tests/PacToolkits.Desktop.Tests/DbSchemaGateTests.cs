using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class DbSchemaGateTests
{
    [Fact]
    public async Task Match_reuses_one_read_for_different_ranges()
    {
        var schema = new CountingSchema("1.2.22");
        var gate = new DbSchemaGate(schema);

        var read = await gate.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, schema.ReadCount);

        Assert.True(gate.Match(read, "1.2.20", "1.2.25").IsCompatible);
        Assert.False(gate.Match(read, "1.2.23", "1.2.25").IsCompatible);
        Assert.Equal(1, schema.ReadCount);
    }

    [Fact]
    public async Task Read_then_match_evaluates_range()
    {
        var gate = new DbSchemaGate(new FixedSchema("1.2.21"));
        var read = await gate.ReadAsync(TestContext.Current.CancellationToken);
        var result = gate.Match(read, "1.2.22", "1.2.22");

        Assert.Equal(DbSchemaCompatibility.BelowMinimum, result.Status);
    }

    private sealed class FixedSchema(string version) : IDbSchemaVersionService
    {
        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionRead(true, version, null));

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
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

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
            => TryReadSchemaVersionAsync(ct);
    }
}
