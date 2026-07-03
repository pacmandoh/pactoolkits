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
        await Task.Delay(50);
        await page.OnPageDeactivatedAsync();
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

        monitor.IsConnected = false;
        page.SyncPageAvailability();

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.True(page.IsShowingStaleData);
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
