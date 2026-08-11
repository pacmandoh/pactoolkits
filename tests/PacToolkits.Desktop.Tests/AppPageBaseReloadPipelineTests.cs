using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
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
    public async Task Sync_after_reconnect_clears_db_stale_when_page_already_loaded()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var page = CreatePage(dbMonitor: monitor);

        await page.TestRunReloadCoreAsync();
        monitor.IsConnected = false;
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);

        monitor.IsConnected = true;
        page.SyncPageAvailability();

        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.True(page.CanPageFromDb);
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
    public async Task Transient_pac_api_error_sets_awaiting_service_without_db_signal()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ => throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                new PacToolkits.Application.DTOs.PacApiProblem(
                    Status: 503,
                    Code: "service_unavailable",
                    Title: "down",
                    Detail: null,
                    TraceId: "t",
                    RetryAfter: null)));

        await page.TestRunReloadCoreAsync();

        Assert.Equal(0, monitor.SignalCount);
        Assert.True(monitor.IsConnected);
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.Equal("等待服务可用", page.PageUnavailableTitle);
        Assert.True(page.ShowPageUnavailable);

        // 本机库仍连着时，Sync 不要清掉 AwaitingService
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);

        // 本机库短暂断开，也不要把 AwaitingService 打成 AwaitingDatabase
        monitor.IsConnected = false;
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        monitor.IsConnected = true;
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
    }

    [Fact]
    public async Task Remote_reload_runs_while_local_db_disconnected()
    {
        var monitor = new FakeDbMonitor { IsConnected = false };
        var attempts = 0;
        var page = CreateRemotePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                return Task.CompletedTask;
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(1, attempts);
        Assert.Equal(0, monitor.SignalCount);
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.True(page.HasLoadedOnce);
        Assert.Equal(string.Empty, page.PageUnavailableTitle);
        Assert.Equal(SectionEmptyCopy.ServiceStaleHint, page.PageStaleHint);
    }

    [Fact]
    public void Remote_page_not_loaded_uses_service_copy()
    {
        var page = CreateRemotePage(dbMonitor: new FakeDbMonitor { IsConnected = false });

        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.Equal("等待服务可用", page.PageUnavailableTitle);
        Assert.Equal("服务恢复后将自动重试", page.PageUnavailableHint);
        Assert.Equal("Server", page.PageUnavailableIcon);
        Assert.Equal(SectionEmptyCopy.ServiceStaleHint, page.PageStaleHint);
    }

    [Fact]
    public async Task Remote_page_reload_skips_db_wait_when_RequiresLocalDbForReload_false()
    {
        var monitor = new FakeDbMonitor { IsConnected = false };
        var attempts = 0;
        var page = CreateRemotePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                return Task.CompletedTask;
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(1, attempts);
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.True(page.CanPageFromDb);
    }

    [Fact]
    public async Task Remote_page_ignores_db_disconnect_auto_refresh()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var attempts = 0;
        var page = CreateRemotePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                return Task.CompletedTask;
            });

        await page.TestRunReloadCoreAsync();
        Assert.Equal(1, attempts);

        monitor.IsConnected = false;
        monitor.RaiseDisconnected();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, attempts);
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
    }

    [Fact]
    public async Task Service_retry_stops_after_page_dispose()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var attempts = 0;
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        var afterFail = attempts;
        Assert.True(afterFail >= 1);

        page.Dispose();
        // 超过注入的重试间隔；若未取消会再打 Reload
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(afterFail, attempts);
    }

    [Fact]
    public async Task Service_retry_resumes_after_page_reactivate()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var attempts = 0;
        var page = CreateRemotePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        var afterFail = attempts;

        // 立即离开再进入：旧任务 finally 不得清掉新一代门闩
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await page.TestOnPageActivatedAsync();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(attempts > afterFail, "expected service retry after immediate reactivate");
    }

    [Fact]
    public async Task Service_retry_stops_after_page_deactivate()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var attempts = 0;
        var page = CreateRemotePage(
            dbMonitor: monitor,
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        var afterFail = attempts;
        Assert.True(afterFail >= 1);

        // 离开页面后旧任务 finally 不得以新 generation 续排
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(afterFail, attempts);
    }

    [Fact]
    public async Task PacApi_transient_skips_page_level_transport_retry()
    {
        var attempts = 0;
        var page = CreateRemotePage(
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "service_unavailable",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(1, attempts);
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
    }

    [Fact]
    public async Task Wrapped_PacApi_transient_skips_page_level_transport_retry()
    {
        var time = new ControllableTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var attempts = 0;
        var page = CreateRemotePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                throw new InvalidOperationException(
                    "wrap",
                    new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                        new PacToolkits.Application.DTOs.PacApiProblem(
                            Status: 503,
                            Code: "service_unavailable",
                            Title: "down",
                            Detail: null,
                            TraceId: "t",
                            RetryAfter: TimeSpan.FromSeconds(12))));
            });

        try
        {
            await page.TestRunReloadCoreAsync();

            Assert.Equal(1, attempts);
            Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
            Assert.Equal(time.GetUtcNow().AddSeconds(12), page.TestNextRetryAt());
        }
        finally
        {
            page.Dispose();
        }
    }

    [Fact]
    public async Task Service_retry_honors_RetryAfter_via_TimeProvider()
    {
        var time = new ControllableTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var attempts = 0;
        var page = CreateRemotePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 429,
                        Code: "rate_limited",
                        Title: "slow down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: TimeSpan.FromSeconds(30)));
            });

        try
        {
            await page.TestRunReloadCoreAsync();
            Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
            Assert.Equal(1, attempts);
            Assert.Equal(time.GetUtcNow().AddSeconds(30), page.TestNextRetryAt());

            // 等 PostOnUi 挂上 Task.Delay(TimeProvider) 定时器
            await Task.Delay(40, TestContext.Current.CancellationToken);

            time.Advance(TimeSpan.FromSeconds(29));
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(1, attempts);

            time.Advance(TimeSpan.FromSeconds(2));
            var deadline = Environment.TickCount64 + 2_000;
            while (attempts <= 1 && Environment.TickCount64 < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(attempts > 1, "expected service retry after Retry-After elapsed");
        }
        finally
        {
            // ControllableTime 上未推进的 Delay 会挂起进程；必须取消服务重试
            page.Dispose();
        }
    }

    [Fact]
    public async Task Later_RetryAfter_reschedules_already_queued_service_retry()
    {
        var time = new ControllableTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var attempts = 0;
        var page = CreateRemotePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                        new PacToolkits.Application.DTOs.PacApiProblem(
                            Status: 503,
                            Code: "transport",
                            Title: "down",
                            Detail: null,
                            TraceId: "t",
                            RetryAfter: null));
                }

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 429,
                        Code: "rate_limited",
                        Title: "slow down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: TimeSpan.FromSeconds(30)));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromSeconds(5));

        try
        {
            await page.TestRunReloadCoreAsync();
            Assert.Equal(1, attempts);
            Assert.Equal(time.GetUtcNow().AddSeconds(5), page.TestNextRetryAt());

            // 5s 任务已排队时手动刷新拿到更晚的 Retry-After，应取消重排
            await page.TestRunReloadCoreAsync();
            Assert.Equal(2, attempts);
            Assert.Equal(time.GetUtcNow().AddSeconds(30), page.TestNextRetryAt());

            await Task.Delay(40, TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(40, TestContext.Current.CancellationToken);
            Assert.Equal(2, attempts);

            time.Advance(TimeSpan.FromSeconds(25));
            var deadline = Environment.TickCount64 + 2_000;
            while (attempts < 3 && Environment.TickCount64 < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(attempts >= 3, "expected retry only after the later Retry-After");
        }
        finally
        {
            page.Dispose();
        }
    }

    [Fact]
    public async Task Service_retry_clears_sticky_RetryAfter_on_headerless_failure()
    {
        var time = new ControllableTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var attempts = 0;
        var page = CreateRemotePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                        new PacToolkits.Application.DTOs.PacApiProblem(
                            Status: 429,
                            Code: "rate_limited",
                            Title: "slow down",
                            Detail: null,
                            TraceId: "t",
                            RetryAfter: TimeSpan.FromSeconds(30)));
                }

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromSeconds(5));

        try
        {
            await page.TestRunReloadCoreAsync();
            Assert.Equal(time.GetUtcNow().AddSeconds(30), page.TestNextRetryAt());

            await Task.Delay(40, TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(30));
            var deadline = Environment.TickCount64 + 2_000;
            while (attempts < 2 && Environment.TickCount64 < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(attempts >= 2);
            // 无头失败应改用默认 5s，不得粘滞旧的 30s
            Assert.Equal(time.GetUtcNow().AddSeconds(5), page.TestNextRetryAt());
        }
        finally
        {
            page.Dispose();
        }
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(30, 30)]
    [InlineData(120, 60)]
    public void ClampRetryAfter_bounds(double seconds, double expectedSeconds)
        => Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            AppPageBase.ClampRetryAfter(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task Transient_pac_api_error_after_load_keeps_stale_not_ready()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
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

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        await page.TestRunReloadCoreAsync();

        Assert.Equal(0, monitor.SignalCount);
        Assert.True(monitor.IsConnected);
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);

        // 本机库仍连着时，Sync 不得清掉已挂服务截止的 PacApi Stale
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());
        Assert.Equal(SectionEmptyCopy.ServiceStaleHint, page.PageStaleHint);
        Assert.Equal("Server", page.SectionEmptyIcon);
    }

    [Fact]
    public async Task Local_page_pac_api_stale_resumes_after_db_flap_without_page_auto_refresh()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
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

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        var afterFail = attempts;

        // 默认不因库重连自动刷页：库抖一下后仍须靠服务重试续排
        monitor.IsConnected = false;
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        monitor.IsConnected = true;
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(attempts > afterFail, "expected service retry after DB flap while PacApi Stale");
    }

    [Fact]
    public async Task Local_page_pac_api_stale_resumes_service_retry_after_reactivate()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
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

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "transport",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        var afterFail = attempts;

        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await page.TestOnPageActivatedAsync();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(attempts > afterFail, "expected PacApi Stale service retry after reactivate on local page");
    }

    [Fact]
    public async Task Local_page_pac_api_fail_while_deactivated_keeps_armed_stale()
    {
        var monitor = new FakeDbMonitor { IsConnected = true };
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ => Task.CompletedTask);
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        // 先离开再失败：Arm 仍须写入截止，Sync 不得把服务 Stale 清成 Ready
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        page.ReloadAction = _ => throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
            new PacToolkits.Application.DTOs.PacApiProblem(
                Status: 503,
                Code: "transport",
                Title: "down",
                Detail: null,
                TraceId: "t",
                RetryAfter: null));
        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());
        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);

        await page.TestOnPageActivatedAsync();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
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
    public async Task Local_db_transport_fail_marks_stale_even_when_monitor_still_connected()
    {
        // Fake 默认同步断连；这里关掉，贴近生产 Signal 只排队探测
        var monitor = new FakeDbMonitor
        {
            IsConnected = true,
            DisconnectOnSignal = false
        };
        var page = CreatePage(
            dbMonitor: monitor,
            reload: _ => Task.CompletedTask);

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        page.ReloadAction = _ => throw new IOException("connection reset");
        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.True(monitor.IsConnected);
        Assert.True(monitor.SignalCount >= 1);
        Assert.Null(page.TestNextRetryAt());
        Assert.Equal(SectionEmptyCopy.DbStaleHint, page.PageStaleHint);
    }

    [Fact]
    public async Task Cancel_loading_service_retry_restores_awaiting_service()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = CreatePage(
            dbMonitor: new FakeDbMonitor { IsConnected = true },
            reload: _ => throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                new PacToolkits.Application.DTOs.PacApiProblem(
                    Status: 503,
                    Code: "transport",
                    Title: "down",
                    Detail: null,
                    TraceId: "t",
                    RetryAfter: null)));
        page.TestSetServiceRetryDelay(TimeSpan.FromMilliseconds(40));

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());

        // 非静默重试进 Loading 后取消，应回到 AwaitingService 并保留截止
        page.ReloadAction = async ct =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };
        var reloadTask = page.TestRunReloadCoreAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await reloadTask;

        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());
    }

    [Fact]
    public async Task Pac_api_then_local_db_fail_clears_service_deadline()
    {
        var monitor = new FakeDbMonitor { IsConnected = true, DisconnectOnSignal = false };
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

                if (attempts == 2)
                {
                    throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.PacApiException(
                        new PacToolkits.Application.DTOs.PacApiProblem(
                            Status: 503,
                            Code: "transport",
                            Title: "down",
                            Detail: null,
                            TraceId: "t",
                            RetryAfter: null));
                }

                throw new IOException("connection reset");
            });

        await page.TestRunReloadCoreAsync();
        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());

        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.Null(page.TestNextRetryAt());
        Assert.Equal(SectionEmptyCopy.DbStaleHint, page.PageStaleHint);
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

    private static RemoteReloadPage CreateRemotePage(
        Func<CancellationToken, Task>? reload = null,
        FakeDbMonitor? dbMonitor = null,
        TimeProvider? timeProvider = null)
    {
        var page = new RemoteReloadPage
        {
            ReloadAction = reload ?? (_ => Task.CompletedTask)
        };
        page.TestInjectDbServices(
            dbMonitor ?? new FakeDbMonitor { IsConnected = false },
            new FakeAccessGuard(),
            new FakeStartupState { IsDbInitCompleted = false },
            timeProvider: timeProvider);
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

    private sealed class RemoteReloadPage : AppPageBase
    {
        public Func<CancellationToken, Task>? ReloadAction { get; set; }

        public override string DisplayName => "Remote";
        public override string Icon => "Activity";
        public override int Index => 98;

        protected override bool RequiresLocalDbForReload => false;

        // 模拟已迁移页仍写了 DB auto-refresh；远端边界应忽略
        protected override bool AutoRefreshOnDbDisconnected => true;
        protected override bool AutoRefreshOnDbReconnected => true;

        protected override Task ReloadCoreAsync(CancellationToken ct)
            => ReloadAction?.Invoke(ct) ?? Task.CompletedTask;
    }

    internal sealed class FakeDbMonitor : IDbConnectionMonitorService
    {
        public bool IsConnected { get; set; }

        public bool ReconnectAfterSignal { get; set; }

        // false：只计数，不立刻 Disconnected（贴近生产 Signal）
        public bool DisconnectOnSignal { get; set; } = true;

        public int MaxReconnectSignals { get; set; } = 1;

        public int SignalCount { get; private set; }

        public event Action? Disconnected;
        public event Action? Reconnected;
        public event Action<string>? ConnectionFailed;

        public void Dispose()
        {
        }

        public void Start()
        {
        }

        public void RaiseDisconnected()
            => Disconnected?.Invoke();

        public void RaiseReconnected()
            => Reconnected?.Invoke();

        public void Signal()
        {
            SignalCount++;
            if (!DisconnectOnSignal)
            {
                return;
            }

            IsConnected = false;
            ConnectionFailed?.Invoke("signal");
            if (ReconnectAfterSignal && SignalCount <= MaxReconnectSignals)
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
