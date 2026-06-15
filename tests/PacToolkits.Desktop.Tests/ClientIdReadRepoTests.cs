using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Desktop.Tests;

public sealed class ClientIdReadRepoTests
{
    [Fact]
    public async Task GetDistinctClientIds_throws_when_guard_is_blocked()
    {
        var guard = new DatabaseAccessGuard();
        guard.Block("数据库版本不兼容");
        var repo = new ClientIdReadRepo(new NullLogger(), guard);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.GetDistinctClientIdsAsync(new PgOptions(), CancellationToken.None));
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pactoolkits-client-id-read-test.log";
        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
