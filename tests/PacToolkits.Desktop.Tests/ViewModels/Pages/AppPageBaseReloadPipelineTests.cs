using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class AppPageBaseReloadPipelineTests
{
    [Fact]
    public void Unavailable_service_before_first_reload_does_not_keep_sections_pending()
    {
        var page = CreateRemotePage(api: FakeApiAvailability.Unavailable());
        page.SyncPageAvailability();

        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.False(page.IsSectionPending);
    }

    [Fact]
    public void Connection_becoming_ready_notifies_can_page_without_availability_change()
    {
        var api = FakeApiAvailability.Connecting();
        var page = CreateRemotePage(api: api);
        var changed = new List<string?>();
        page.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        page.SyncPageAvailability();
        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.False(page.IsSectionPending);
        Assert.True(page.TestShowEmpty());
        Assert.Equal("正在检查 PacAPI 服务，完成后将自动加载", page.TestEmptyHint());
        Assert.False(page.CanPage);

        api.Current = FakeApiAvailability.Ready().Current;
        page.SyncConnection(api.Current);

        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.True(page.IsSectionPending);
        Assert.True(page.CanPage);
        Assert.Contains(nameof(AppPageBase.CanPage), changed);
    }

    [Fact]
    public async Task Silent_first_reload_keeps_sections_pending_until_data_is_ready()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = CreatePage();

        using var silent = page.TestBeginSilentReload();
        var reload = page.TestRunReloadAsync(async ct =>
        {
            started.SetResult();
            await release.Task.WaitAsync(ct);
        });
        await started.Task;

        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.True(page.IsSectionPending);
        Assert.False(page.IsBusy);

        release.SetResult();
        await reload;

        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.False(page.IsSectionPending);
    }

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
    public void SyncConnection_hard_block_sets_access_blocked_on_remote_page()
    {
        var page = CreateRemotePage();
        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.ContractBlocked,
            Detail: "contract boom",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.AccessBlocked, page.PageDataAvailability);
        Assert.Equal("PacAPI 服务协议不兼容", page.PageUnavailableTitle);
        Assert.Equal("contract boom", page.PageUnavailableHint);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public void SyncConnection_schema_block_sets_access_blocked_on_remote_page()
    {
        var page = CreateRemotePage();
        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.SchemaBlocked,
            Detail: "服务端数据库结构不兼容",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.AccessBlocked, page.PageDataAvailability);
        Assert.Equal("PacAPI 服务数据库结构不兼容", page.PageUnavailableTitle);
        Assert.False(page.IsBusy);
        Assert.True(page.ShowPageUnavailable);
    }

    [Fact]
    public void SyncConnection_server_database_down_is_awaiting_service_not_blocked()
    {
        var page = CreateRemotePage();
        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.ServerDatabaseBlocked,
            Detail: "PacAPI 服务已连接，但服务端数据库不可用",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.False(page.IsBusy);
        Assert.False(page.ShowPageUnavailable);
    }

    [Fact]
    public void SyncConnection_unavailable_sets_awaiting_service_on_remote_page()
    {
        var page = CreateRemotePage();
        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Unavailable,
            Detail: "connection refused",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.False(page.ShowPageUnavailable);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public void SyncConnection_ready_clears_access_blocked_on_remote_page()
    {
        var page = CreateRemotePage();
        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.SchemaBlocked,
            Detail: "schema boom",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));
        Assert.Equal(PageDataAvailability.AccessBlocked, page.PageDataAvailability);

        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Ready,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.False(page.ShowPageUnavailable);
    }

    [Fact]
    public void SyncConnection_ready_clears_awaiting_service_on_remote_page()
    {
        var page = CreateRemotePage();
        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Unavailable,
            Detail: "down",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.False(page.ShowPageUnavailable);

        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Ready,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.False(page.ShowPageUnavailable);
        Assert.True(page.CanPage);
    }

    [Fact]
    public async Task SyncConnection_ready_clears_stale_on_remote_page()
    {
        var page = CreateRemotePage(reload: _ => Task.CompletedTask);
        await page.TestRunReloadCoreAsync();
        Assert.True(page.HasLoadedOnce);

        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Unavailable,
            Detail: "down",
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));
        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.False(page.IsBusy);

        page.SyncConnection(new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Ready,
            Detail: null,
            CheckedAt: DateTimeOffset.UtcNow,
            FirstCheckCompleted: true));

        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
        Assert.False(page.ShowPageUnavailable);
        Assert.False(page.IsShowingStaleData);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public void Remote_page_not_loaded_uses_service_copy()
    {
        var page = CreateRemotePage();

        Assert.Equal(PageDataAvailability.NotLoaded, page.PageDataAvailability);
        Assert.False(page.ShowPageUnavailable);
        Assert.Equal(SectionEmptyCopy.StaleHint, page.PageStaleHint);
    }

    [Fact]
    public async Task Remote_page_reload_skips_fetch_when_api_unavailable()
    {
        var attempts = 0;
        var page = CreateRemotePage(
            api: FakeApiAvailability.Unavailable(),
            reload: _ =>
            {
                attempts++;
                return Task.CompletedTask;
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(0, attempts);
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.False(page.IsBusy);
        Assert.False(page.CanPage);
    }

    [Fact]
    public void Remote_page_CanToastError_false_when_api_down()
    {
        var down = CreateRemotePage(api: FakeApiAvailability.Unavailable());
        Assert.False(down.TestCanToastError(new InvalidOperationException("boom")));

        var up = CreateRemotePage();
        Assert.True(up.TestCanToastError(new InvalidOperationException("business")));
    }

    [Fact]
    public void Remote_page_lookup_catalog_not_suspended()
    {
        var page = CreateRemotePage();
        Assert.False(page.TestIsLookupCatalogSuspended());
    }

    [Fact]
    public async Task Service_retry_stops_after_page_dispose()
    {
        var attempts = 0;
        var page = CreatePage(
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(afterFail, attempts);
    }

    [Fact]
    public async Task Service_retry_resumes_after_page_reactivate()
    {
        var attempts = 0;
        var page = CreatePage(
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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

        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await page.TestOnPageActivatedAsync();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(attempts > afterFail, "expected service retry after immediate reactivate");
    }

    [Fact]
    public async Task Service_retry_stops_after_page_deactivate()
    {
        var attempts = 0;
        var page = CreatePage(
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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

        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(afterFail, attempts);
    }

    [Fact]
    public async Task Cancel_loading_service_retry_restores_awaiting_service()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = CreatePage(
            reload: _ => throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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
    public async Task PacApi_transient_while_up_arms_service_retry()
    {
        var attempts = 0;
        var page = CreateRemotePage(
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "service_unavailable",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: TimeSpan.FromSeconds(12)));
            });

        await page.TestRunReloadCoreAsync();

        Assert.Equal(1, attempts);
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task PacApi_transient_while_down_skips_page_level_retry()
    {
        var attempts = 0;
        var page = CreateRemotePage(
            api: FakeApiAvailability.Unavailable(),
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
                    new PacToolkits.Application.DTOs.PacApiProblem(
                        Status: 503,
                        Code: "service_unavailable",
                        Title: "down",
                        Detail: null,
                        TraceId: "t",
                        RetryAfter: null));
            });

        // Down 时预检不拉取数据，也不安排页面重试
        await page.TestRunReloadCoreAsync();

        Assert.Equal(0, attempts);
        Assert.Equal(PageDataAvailability.AwaitingService, page.PageDataAvailability);
        Assert.Null(page.TestNextRetryAt());
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task Remote_transport_fail_while_ready_goes_stale_not_ready()
    {
        var page = CreateRemotePage(reload: _ => Task.CompletedTask);
        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        page.ReloadAction = _ => throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
            new PacToolkits.Application.DTOs.PacApiProblem(
                Status: 503,
                Code: "service_unavailable",
                Title: "down",
                Detail: null,
                TraceId: "t",
                RetryAfter: null));

        await page.TestRunReloadCoreAsync();

        Assert.Equal(PageDataAvailability.Stale, page.PageDataAvailability);
        Assert.NotNull(page.TestNextRetryAt());
        Assert.False(page.IsBusy);
        Assert.False(page.ShowPageUnavailable);
    }

    [Fact]
    public async Task Remote_manual_reload_enters_loading_without_immediate_busy()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = CreateRemotePage(reload: async ct =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        var run = page.TestRunReloadCoreAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(PageDataAvailability.Loading, page.PageDataAvailability);
        Assert.False(page.IsBusy);

        await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
        await run;
    }

    [Fact]
    public async Task Remote_signal_reload_does_not_enter_loading_or_busy()
    {
        var page = CreateRemotePage(reload: _ => Task.CompletedTask);
        await page.TestRunReloadCoreAsync();
        Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.ReloadAction = async ct =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };

        using (page.TestBeginSilentReload())
        {
            var run = page.TestRunReloadCoreAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.Equal(PageDataAvailability.Ready, page.PageDataAvailability);
            Assert.False(page.IsBusy);

            await page.OnPageDeactivatedAsync(TestContext.Current.CancellationToken);
            await run;
        }
    }

    [Fact]
    public async Task Wrapped_PacApi_transient_skips_page_level_transport_retry()
    {
        var time = new ControllableTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var attempts = 0;
        var page = CreatePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                throw new InvalidOperationException(
                    "wrap",
                    new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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
        var page = CreatePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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
        var page = CreatePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
                        new PacToolkits.Application.DTOs.PacApiProblem(
                            Status: 503,
                            Code: "transport",
                            Title: "down",
                            Detail: null,
                            TraceId: "t",
                            RetryAfter: null));
                }

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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
        var page = CreatePage(
            timeProvider: time,
            reload: _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
                        new PacToolkits.Application.DTOs.PacApiProblem(
                            Status: 429,
                            Code: "rate_limited",
                            Title: "slow down",
                            Detail: null,
                            TraceId: "t",
                            RetryAfter: TimeSpan.FromSeconds(30)));
                }

                throw new PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api.PacApiException(
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

    private static TestReloadPage CreatePage(
        Func<CancellationToken, Task>? reload = null,
        TimeProvider? timeProvider = null,
        FakeApiAvailability? api = null)
    {
        var page = new TestReloadPage
        {
            ReloadAction = reload ?? (_ => Task.CompletedTask)
        };
        page.TestInjectServices(
            timeProvider: timeProvider,
            apiAvailability: api ?? FakeApiAvailability.Ready());
        return page;
    }

    private static RemoteReloadPage CreateRemotePage(
        Func<CancellationToken, Task>? reload = null,
        TimeProvider? timeProvider = null,
        FakeApiAvailability? api = null)
    {
        var page = new RemoteReloadPage
        {
            ReloadAction = reload ?? (_ => Task.CompletedTask)
        };
        page.TestInjectServices(
            timeProvider: timeProvider,
            apiAvailability: api ?? FakeApiAvailability.Ready());
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

        protected override Task ReloadCoreAsync(CancellationToken ct)
            => ReloadAction?.Invoke(ct) ?? Task.CompletedTask;

        public bool TestShowEmpty() => ShowSectionEmpty(isContentEmpty: true);

        public string TestEmptyHint() => GetSectionEmptyHint(readyHint: "暂无数据");
    }

    internal sealed class FakeApiAvailability : IApiAvailabilityService
    {
        public ApiAvailabilitySnapshot Current { get; set; }

        public bool IsConfigured { get; set; } = true;

        public string? LastApiVersion { get; set; }

        public string? LastContractVersion { get; set; }

        public string? LastDatabase { get; set; }

        public string? LastSchema { get; set; }

        public string? LastSchemaVersion { get; set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public FakeApiAvailability(ApiAvailabilitySnapshot current)
            => Current = current;

        public static FakeApiAvailability Ready()
            => new(new ApiAvailabilitySnapshot(
                ApiAvailabilityState.Ready,
                Detail: null,
                CheckedAt: DateTimeOffset.UtcNow,
                FirstCheckCompleted: true));

        public static FakeApiAvailability Unavailable()
            => new(new ApiAvailabilitySnapshot(
                ApiAvailabilityState.Unavailable,
                Detail: "down",
                CheckedAt: DateTimeOffset.UtcNow,
                FirstCheckCompleted: true));

        public static FakeApiAvailability Connecting()
            => new(new ApiAvailabilitySnapshot(
                ApiAvailabilityState.Connecting,
                Detail: null,
                CheckedAt: DateTimeOffset.UtcNow,
                FirstCheckCompleted: false));

        public static FakeApiAvailability Unconfigured()
            => new(new ApiAvailabilitySnapshot(
                ApiAvailabilityState.Connecting,
                Detail: null,
                CheckedAt: DateTimeOffset.UtcNow,
                FirstCheckCompleted: false))
            {
                IsConfigured = false,
            };

        public void Start()
        {
        }

        public Task ProbeAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public void Notify()
        {
        }

        public void Dispose()
        {
        }
    }
}
