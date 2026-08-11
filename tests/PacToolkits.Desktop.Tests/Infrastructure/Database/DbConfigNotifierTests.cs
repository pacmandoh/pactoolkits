using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Desktop.Tests;

public sealed class DbConfigNotifierTests
{
    [Fact]
    public void Applied_forwards_db_config_service_event()
    {
        var dbConfig = new FakeDbConfigService();
        var notifier = new DbConfigNotifier(dbConfig);
        var applyCount = 0;
        notifier.Applied += (_, _) => applyCount++;

        dbConfig.RaiseApplied();

        Assert.Equal(1, applyCount);
    }

    private sealed class FakeDbConfigService : IDbConfigService
    {
        public PgOptions Current { get; } = new();
        public string ConfigPath => "/tmp/pactoolkits-test.config.json";
        public event EventHandler? Applied;

        public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(true);

        public Task ApplyAsync(PgOptions opt, CancellationToken ct = default)
            => Task.CompletedTask;

        public void RaiseApplied()
            => Applied?.Invoke(this, EventArgs.Empty);
    }
}
