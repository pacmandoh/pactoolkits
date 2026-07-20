using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class AppPageBaseReloadPipelineTests
{
    [Fact]
    public async Task Successful_reload_sets_ready_and_has_loaded_once()
    {
        var page = CreatePage();

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.True(page.HasLoadedOnce);
    }

    [Fact]
    public async Task Non_transport_failure_sets_load_failed()
    {
        var page = CreatePage(reload: _ => throw new InvalidOperationException("boom"));

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.LoadFailed, page.PageDataAvailability);
        Assert.Equal("boom", page.PageUnavailableHint);
    }

    [Fact]
    public async Task Access_blocked_skips_fetch_and_sets_access_blocked()
    {
        var guard = new FakeAccessGuard();
        guard.Block("schema mismatch");
        var page = CreatePage(accessGuard: guard, reload: _ => throw new InvalidOperationException("should not run"));

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.AccessBlocked, page.PageDataAvailability);
        Assert.Equal("schema mismatch", page.PageUnavailableTitle);
    }

    [Fact]
    public async Task Cancel_during_disconnected_wait_keeps_awaiting_database_before_first_load()
    {
        var monitor = new FakeDbMonitor { IsConnected = false };
        var page = CreatePage(dbMonitor: monitor);

        var run = page.TestRunReloadCoreAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await run;

        Assert.Equal(PageDataAvailability.AwaitingDatabase, page.PageDataAvailability);
        Assert.False(page.HasLoadedOnce);
    }

    [Fact]
    public async Task Sync_after_disconnect_shows_stale_when_page_already_loaded()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var page = CreatePage(dbMonitor: monitor);

        await page.TestRunReloadCoreAsync();

        Assert.True(page.CanPageFromDb);

        monitor.IsConnected = false;
        page.SyncPageAvailability();

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.True(page.IsShowingStaleData);
        Assert.False(page.CanPageFromDb);
    }

    [Fact]
    public async Task Transport_retry_when_reconnect_wait_fails_does_not_mark_ready()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var attempts = 0;
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                throw new IOException("connection reset");
            });

        var run = page.TestRunReloadCoreAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await run;

        Assert.Equal(PageDataAvailability.AwaitingDatabase, page.PageDataAvailability);
        Assert.False(page.HasLoadedOnce);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Transport_retry_when_reconnect_wait_fails_keeps_stale_after_first_load()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var attempts = 0;
        var page = CreatePage(
            dbMonitor: monitor,
            reload: ct =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.CompletedTask;
                }

                throw new IOException("connection reset");
            });

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        var run = page.TestRunReloadCoreAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await run;

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.True(page.HasLoadedOnce);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Transport_retry_exhaustion_before_first_load_does_not_mark_ready()
    {
        var monitor = new FakeDbMonitor
        {
            IsConnected = true,
            ReconnectAfterSignal = true,
            MaxReconnectSignals = 1
        };
        var attempts = 0;
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                throw new IOException("connection reset");
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.AwaitingDatabase, page.PageDataAvailability);
        Assert.False(page.HasLoadedOnce);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Transport_retry_exhaustion_after_first_load_keeps_stale_not_ready()
    {
        var monitor = new FakeDbMonitor
        {
            IsConnected = true,
            ReconnectAfterSignal = true,
            MaxReconnectSignals = 1
        };
        var attempts = 0;
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.CompletedTask;
                }

                throw new IOException("connection reset");
            });

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        var reloadAttempts = 0;
        page.ReloadAction = _ =>
        {
            reloadAttempts++;
            throw new IOException("connection reset");
        };

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.NotEqual(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.True(page.HasLoadedOnce);
        Assert.Equal(2, reloadAttempts);
    }

    [Fact]
    public async Task Transport_retry_succeeds_on_second_attempt_marks_ready()
    {
        var monitor = new FakeDbMonitor
        {
            IsConnected = true,
            ReconnectAfterSignal = true,
            MaxReconnectSignals = 1
        };
        var attempts = 0;
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new IOException("connection reset");
                }

                return Task.CompletedTask;
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.True(page.HasLoadedOnce);
        Assert.Equal(2, attempts);
    }

    private static TestReloadPage CreatePage(
        Func<CancellationToken, Task>? reload = null,
        FakeDbMonitor? dbMonitor = null,
        FakeAccessGuard? accessGuard = null,
        FakeStartupState? startupState = null)
    {
        var page = new TestReloadPage
        {
            ReloadAction = reload ?? (_ => Task.CompletedTask)
        };
        page.TestInjectDbServices(
            dbMonitor ?? new FakeDbMonitor { IsConnected = true },
            accessGuard ?? new FakeAccessGuard(),
            startupState ?? new FakeStartupState { IsDbInitCompleted = true });
        return page;
    }

    private sealed class TestReloadPage : AppPageBase
    {
        public Func<CancellationToken, Task>? ReloadAction { get; set; }

        public override string DisplayName => "Test";
        public override string Icon => "Activity";
        public override int Index => 99;

        protected override Task ReloadCoreAsync(CancellationToken ct)
            => ReloadAction?.Invoke(ct) ?? Task.CompletedTask;
    }

    internal sealed class FakeDbMonitor : IDbConnectionMonitorService
    {
        public bool IsConnected { get; set; }

        public bool ReconnectAfterSignal { get; set; }

        public int MaxReconnectSignals { get; set; } = 1;

        private int _signalCount;

#pragma warning disable CS0067
        public event Action? Disconnected;
        public event Action? Reconnected;
        public event Action<string>? ConnectionFailed;
#pragma warning restore CS0067

        public void Dispose()
        {
        }

        public void Start()
        {
        }

        public void Signal()
        {
            IsConnected = false;
            _signalCount++;
            if (ReconnectAfterSignal && _signalCount <= MaxReconnectSignals)
            {
                IsConnected = true;
                Reconnected?.Invoke();
            }
        }

        public Task<DbProbeReport> ProbeAsync(DbProbeKind kind, CancellationToken ct)
            => Task.FromResult(new DbProbeReport(kind, true, null));
    }

    private sealed class FakeAccessGuard : IDbAccessGuard
    {
        public bool IsBlocked { get; private set; }

        public string? BlockReason { get; private set; }

        public void Block(string reason)
        {
            IsBlocked = true;
            BlockReason = reason;
        }

        public void Clear()
        {
            IsBlocked = false;
            BlockReason = null;
        }

        public void ThrowIfBlocked()
        {
            if (IsBlocked)
            {
                throw new InvalidOperationException(BlockReason ?? "blocked");
            }
        }
    }

    private sealed class FakeStartupState : IAppStartupStateService
    {
        public bool IsDbInitCompleted { get; init; } = true;

        public event Action? DbInitCompleted;

        public void MarkDbInitCompleted()
        {
            DbInitCompleted?.Invoke();
        }
    }
}
