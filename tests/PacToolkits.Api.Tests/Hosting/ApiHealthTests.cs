using Microsoft.Extensions.Options;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Api.Tests;

public sealed class ApiHealthTests
{
    [Fact]
    public async Task CheckAsync_caches_probe_within_ttl()
    {
        var db = new CountingDbConfig();
        var health = CreateHealth(db);

        _ = await health.CheckAsync(TestContext.Current.CancellationToken);
        _ = await health.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, db.ProbeCount);
    }

    [Fact]
    public async Task CheckAsync_does_not_cache_failed_probe()
    {
        var db = new CountingDbConfig { Reachable = false };
        var health = CreateHealth(db);

        _ = await health.CheckAsync(TestContext.Current.CancellationToken);
        _ = await health.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, db.ProbeCount);
    }

    [Fact]
    public async Task CheckAsync_single_flight_under_concurrency()
    {
        var db = new CountingDbConfig { Delay = TimeSpan.FromMilliseconds(150) };
        var health = CreateHealth(db);

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => health.CheckAsync(TestContext.Current.CancellationToken))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, db.ProbeCount);
        Assert.All(tasks, static t => Assert.True(t.Result.Ok));
    }

    private static ApiHealth CreateHealth(CountingDbConfig db)
        => new(
            db,
            new CompatibleSchemaGate(),
            Options.Create(new SchemaBoundsOptions
            {
                MinDbSchema = "1.2.25",
                MaxDbSchema = "1.2.25",
            }),
            TimeProvider.System);

    private sealed class CountingDbConfig : IDbConfigService
    {
        private int _probes;

        public TimeSpan Delay { get; init; }

        public int ProbeCount => Volatile.Read(ref _probes);

        public PgOptions Current { get; } = new();

        public bool Reachable { get; init; } = true;

        public async Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
        {
            Interlocked.Increment(ref _probes);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct).ConfigureAwait(false);
            }

            return Reachable;
        }
    }

    private sealed class CompatibleSchemaGate : IDbSchemaGate
    {
        public Task<DbSchemaVersionRead> ReadAsync(CancellationToken ct = default)
            => Task.FromResult(new DbSchemaVersionRead(Ok: true, Value: "1.2.25", Reason: null));

        public DbSchemaCompatibilityResult Match(DbSchemaVersionRead schema, string min, string max)
            => new(DbSchemaCompatibility.Compatible, "1.2.25", min, max, string.Empty);
    }
}
