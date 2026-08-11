using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Desktop.Tests;

public sealed class DbConnectionMonitorProbeTests
{
    [Fact]
    public async Task Open_ok_but_scalar_fail_does_not_mark_connected()
    {
        var monitor = new DbConnectionMonitorService(
            new StubDbConfig(new PgOptions { MonitorPingSeconds = 0, ReconnectIntervalSeconds = 0 }),
            new NullLogger(),
            TimeProvider.System);

        var reconnected = 0;
        monitor.Reconnected += () => Interlocked.Increment(ref reconnected);

        monitor.OpenForTests = static (_, _) =>
            Task.FromResult(new NpgsqlConnection("Host=127.0.0.1;Database=x;Username=x;Password=x"));
        monitor.ScalarForTests = static (_, _, _) =>
            throw new InvalidOperationException("scalar boom");

        try
        {
            var report = await monitor.ProbeAsync(DbProbeKind.HealthCheck, TestContext.Current.CancellationToken);

            Assert.False(report.Success);
            Assert.False(monitor.IsConnected);
            Assert.Equal(0, reconnected);
        }
        finally
        {
            monitor.Dispose();
        }
    }

    [Fact]
    public async Task Successful_reprobe_while_connected_does_not_raise_reconnected()
    {
        var monitor = new DbConnectionMonitorService(
            new StubDbConfig(new PgOptions { MonitorPingSeconds = 0, ReconnectIntervalSeconds = 0 }),
            new NullLogger(),
            TimeProvider.System);

        var reconnected = 0;
        monitor.Reconnected += () => Interlocked.Increment(ref reconnected);
        monitor.OpenForTests = static (_, _) =>
            Task.FromResult(new NpgsqlConnection("Host=127.0.0.1;Database=x;Username=x;Password=x"));
        monitor.ScalarForTests = static (_, _, _) => Task.CompletedTask;

        try
        {
            var first = await monitor.ProbeAsync(DbProbeKind.HealthCheck, TestContext.Current.CancellationToken);
            Assert.True(first.Success);
            Assert.True(monitor.IsConnected);
            Assert.Equal(1, reconnected);

            var second = await monitor.ProbeAsync(DbProbeKind.HealthCheck, TestContext.Current.CancellationToken);
            Assert.True(second.Success);
            Assert.True(monitor.IsConnected);
            Assert.Equal(1, reconnected);
        }
        finally
        {
            monitor.Dispose();
        }
    }

    [Fact]
    public async Task Scalar_timeout_oce_keeps_loop_alive_for_next_probe()
    {
        var monitor = new DbConnectionMonitorService(
            new StubDbConfig(new PgOptions { MonitorPingSeconds = 0, ReconnectIntervalSeconds = 0 }),
            new NullLogger(),
            TimeProvider.System);

        var allowSuccess = 0;
        monitor.OpenForTests = static (_, _) =>
            Task.FromResult(new NpgsqlConnection("Host=127.0.0.1;Database=x;Username=x;Password=x"));
        monitor.ScalarForTests = (_, _, _) =>
        {
            // 服务 token 未取消时抛 OCE，模拟 CancelAfter 探测超时
            if (Volatile.Read(ref allowSuccess) == 0)
            {
                throw new OperationCanceledException();
            }

            return Task.CompletedTask;
        };

        try
        {
            var failed = await monitor.ProbeAsync(DbProbeKind.HealthCheck, TestContext.Current.CancellationToken);
            Assert.False(failed.Success);
            Assert.Contains("超时", failed.Reason, StringComparison.Ordinal);
            Assert.False(monitor.IsConnected);

            Volatile.Write(ref allowSuccess, 1);

            var ok = await monitor.ProbeAsync(DbProbeKind.HealthCheck, TestContext.Current.CancellationToken);
            Assert.True(ok.Success);
            Assert.True(monitor.IsConnected);
        }
        finally
        {
            monitor.Dispose();
        }
    }

    private sealed class StubDbConfig(PgOptions current) : IDbConfigService
    {
        public PgOptions Current { get; } = current;

        public string ConfigPath => string.Empty;

        public event EventHandler? Applied
        {
            add { }
            remove { }
        }

        public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(false);

        public Task ApplyAsync(PgOptions opt, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => string.Empty;

        public string CurrentLogPath => string.Empty;

        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Warn(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Error(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Fatal(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
