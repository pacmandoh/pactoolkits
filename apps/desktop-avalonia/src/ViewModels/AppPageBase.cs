using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Presentation;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// 协调 Desktop 页面重载、数据可用性、数据库连接信号和页面生命周期
///
/// 具体业务查询与 DataGrid 行模型由派生页面负责
/// </summary>
public abstract partial class AppPageBase : ViewModelBase, ITopBarActions, IPageLifecycleAware, IDisposable
{
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
    public abstract int Index { get; }
    public virtual int? BadgeCount => null;
    public virtual bool IsEnabled => true;
    public virtual bool ShowInSidebar => true;

    public virtual string FunctionAreaId => ShellFunctionAreas.TraceabilityId;

    public virtual string SidebarRoute =>
        GetType().Name.EndsWith("ViewModel", StringComparison.Ordinal)
            ? GetType().Name[..^"ViewModel".Length]
            : GetType().Name;

    private readonly IAsyncRelayCommand _refreshCommand;

    public virtual ICommand? RefreshCommand => _refreshCommand;
    public virtual ICommand? ImportCommand => null;
    public virtual ICommand? ExportCommand => null;

    protected virtual bool AutoRefreshOnDbDisconnected => false;
    protected virtual bool AutoRefreshOnDbReconnected => false;
    protected virtual bool SupportsStaleWhileReconnect => true;
    protected virtual bool CanAutoRefreshFromDbSignal() => IsEnabled && RefreshCommand is not null;

    /// <summary>
    /// 是否依赖本机 Pg。为 false 时跳过门禁、startup 与重连等待，也不订阅 DB monitor
    /// </summary>
    protected virtual bool RequiresLocalDbForReload => true;

    private readonly PageReload _reload = new();
    private readonly ConcurrentDictionary<string, byte> _uiCoalesceGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan ReconnectSettleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReconnectToastSuppressWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultServiceRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinRetryAfterDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryAfterDelay = TimeSpan.FromSeconds(60);
    private TimeSpan _serviceRetryDelay = DefaultServiceRetryDelay;
    // 绝对截止（TimeProvider）；每次终态失败替换，成功清空
    private DateTimeOffset? _nextRetryAt;
    private DateTimeOffset? _scheduledRetryAt;
    private DateTimeOffset _reconnectToastSuppressUntil = DateTimeOffset.MinValue;

    private IDbConnectionMonitorService? _cachedDbMonitor;
    private IAppStartupStateService? _cachedStartupState;
    private IDbAccessGuard? _cachedAccessGuard;
    private TimeProvider? _cachedTime;
    private bool _dbMonitorEventsHooked;
    private int _dbSignalRefreshQueued;
    private readonly object _serviceRetryGate = new();
    // 默认允许；离开页面 Cancel 清掉，避免非活动页续排
    private bool _serviceRetryAllowed = true;
    private bool _serviceRetryQueued;
    private int _serviceRetryGeneration;
    private CancellationTokenSource _serviceRetryCts = new();

    private PageDataAvailability _pageDataAvailability = PageDataAvailability.NotLoaded;
    private string? _accessBlockedReason;
    private string? _loadFailedMessage;
    private bool _hasLoadedOnce;
    private bool _isBusy;
    private bool _reloadFromSignal;

    protected bool IsSignalReload => _reloadFromSignal;

    protected bool IsPageReloadActive => _reload.IsActive;

    public bool HasLoadedOnce => _hasLoadedOnce;

    public PageDataAvailability PageDataAvailability => _pageDataAvailability;

    public bool IsShowingStaleData => _pageDataAvailability == PageDataAvailability.Stale;

    public bool CanPageFromDb => RequiresLocalDbForReload
        ? IsDbConnected && !IsDbAccessBlocked(out _)
        : _pageDataAvailability is PageDataAvailability.Ready
            or PageDataAvailability.Stale
            or PageDataAvailability.Loading;

    public string PageStaleHint => SectionEmptyCopy.GetStaleHint(UseServiceStaleCopy);

    public bool ShowPageUnavailable => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => true,
        PageDataAvailability.LoadFailed => true,
        PageDataAvailability.AwaitingDatabase => !_hasLoadedOnce,
        PageDataAvailability.AwaitingService => !_hasLoadedOnce,
        PageDataAvailability.NotLoaded => !_hasLoadedOnce,
        _ => false
    };

    public string PageUnavailableTitle => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => string.IsNullOrWhiteSpace(_accessBlockedReason)
            ? "数据库不可用"
            : _accessBlockedReason!,
        PageDataAvailability.LoadFailed => "加载失败",
        PageDataAvailability.AwaitingDatabase => "等待数据库连接",
        PageDataAvailability.AwaitingService => "等待服务可用",
        PageDataAvailability.NotLoaded => RequiresLocalDbForReload ? "等待数据库连接" : "等待服务可用",
        _ => string.Empty
    };

    public string? PageUnavailableHint => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => "请前往设置检查数据库版本，必要时由服务器端部署工具更新",
        PageDataAvailability.LoadFailed => _loadFailedMessage ?? "请稍后重试，或使用顶部菜单刷新",
        PageDataAvailability.AwaitingDatabase => "连接恢复后将自动加载",
        PageDataAvailability.AwaitingService => "服务恢复后将自动重试",
        PageDataAvailability.NotLoaded => RequiresLocalDbForReload
            ? "连接恢复后将自动加载"
            : "服务恢复后将自动重试",
        _ => null
    };

    public string PageUnavailableIcon => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => "ShieldAlert",
        PageDataAvailability.LoadFailed => "CircleAlert",
        PageDataAvailability.AwaitingService => "Server",
        PageDataAvailability.NotLoaded when !RequiresLocalDbForReload => "Server",
        _ => "Database"
    };

    public string SectionEmptyIcon => SectionEmptyCopy.GetIcon(_pageDataAvailability, UseServiceStaleCopy);

    public bool IsSectionPending => SectionEmptyPolicy.IsPending(_pageDataAvailability, _hasLoadedOnce);

    private bool UseServiceStaleCopy
        => !RequiresLocalDbForReload || HasArmedServiceRetry();

    protected string GetSectionEmptyTitle(string? readyTitle)
        => SectionEmptyCopy.GetTitle(readyTitle);

    protected string GetSectionEmptyHint(string? readyHint)
        => SectionEmptyCopy.GetHint(
            _pageDataAvailability,
            readyHint,
            _accessBlockedReason,
            _loadFailedMessage,
            UseServiceStaleCopy);

    public bool IsBusy
    {
        get => _isBusy;
        protected set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnBusyChanged(value);
                _refreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    protected virtual void OnBusyChanged(bool isBusy) { }

    protected bool IsDbConnected => _cachedDbMonitor?.IsConnected == true;

    protected bool ShowSectionEmpty(bool isContentEmpty)
        => SectionEmptyPolicy.Show(isContentEmpty, _pageDataAvailability, _hasLoadedOnce);

    protected AppPageBase()
    {
        _refreshCommand = new AsyncRelayCommand(
            execute: ExecuteRefreshAsync,
            canExecute: CanRefresh);

        // 仅本机 Pg 页订阅 monitor；远端页不挂 DB 事件
        PostOnUi(() =>
        {
            if (RequiresLocalDbForReload)
            {
                _ = GetDbMonitor();
                _ = GetStartupState();
            }
        }, DispatcherPriority.Background);
    }

    protected virtual Task ReloadCoreAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual void OnReloadFinished() { }

    public virtual Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        lock (_serviceRetryGate)
        {
            _serviceRetryAllowed = true;
        }

        SyncPageAvailability();
        ResumeServiceRetryIfNeeded();
        return Task.CompletedTask;
    }

    public virtual Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelPendingReload();
        CancelServiceRetry();
        return Task.CompletedTask;
    }

    protected void CancelPendingReload()
        => _reload.CancelActiveRun();

    public virtual ValueTask DisposePageAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    protected Task RefreshPageAsync() => _refreshCommand.ExecuteAsync(null);

    protected static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    protected static void RefreshCommands(params IRelayCommand?[] commands)
    {
        PostOnUi(() =>
        {
            foreach (var command in commands)
            {
                command?.NotifyCanExecuteChanged();
            }
        });
    }

    protected static Task RunOnUiAsync(Action action)
        => UiThreadHelper.RunOnUiAsync(action, DispatcherPriority.Background);

    protected static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
        => await UiThreadHelper.RunOnUiAsync(action, priority);

    protected static void PostOnUi(Action action)
        => UiThreadHelper.PostOnUi(action, DispatcherPriority.Background);

    protected static void PostOnUi(Action action, DispatcherPriority priority)
        => UiThreadHelper.PostOnUi(action, priority);

    protected void RefreshCommandsCoalesced(string gateKey, Action refreshAction)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            refreshAction();
            return;
        }

        if (!_uiCoalesceGates.TryAdd(gateKey, 0))
        {
            return;
        }

        PostOnUi(() =>
        {
            _uiCoalesceGates.TryRemove(gateKey, out _);
            refreshAction();
        }, DispatcherPriority.Background);
    }

    private bool CanRefresh() => IsEnabled;

    protected void LogWarn(string eventName, string message, Exception? ex = null, object? context = null)
        => AppLog.Warn(GetType().Name, eventName, message, ex, context);

    protected void LogError(string eventName, string message, Exception? ex = null, object? context = null)
        => AppLog.Error(GetType().Name, eventName, message, ex, context);

    protected void LogInfo(string eventName, string message, object? context = null)
        => AppLog.Info(GetType().Name, eventName, message, context);

    protected void ObserveDetached(Task task, string eventName, string? message = null)
        => TaskObserve.Observe(task, GetType().Name, eventName, message ?? "Detached task failed");

    private void LogReloadStarted()
        => LogInfo("reload.started", "Page reload started", new
        {
            availability = _pageDataAvailability.ToString(),
            fromSignal = _reloadFromSignal,
            hasLoadedOnce = _hasLoadedOnce
        });

    private void LogReloadSkipped(string reason)
        => LogInfo("reload.skipped", "Page reload skipped", new
        {
            reason,
            availability = _pageDataAvailability.ToString()
        });

    private void LogReloadFinished(string outcome, long durationMs)
        => LogInfo("reload.finished", "Page reload finished", new
        {
            outcome,
            durationMs,
            availability = _pageDataAvailability.ToString(),
            hasLoadedOnce = _hasLoadedOnce
        });

    private async Task ExecuteRefreshAsync()
    {
        try
        {
            await RunReloadAsync(ReloadCoreAsync, onFinished: OnReloadFinished).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogError("reload.execute.fail", "Page refresh execution failed", ex);
            HandleReloadException(ex);
        }
    }

    protected Task RunReloadAsync(
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        return _reload.RunAsync(
            ct => RunReloadPipelineAsync(ct, v => IsBusy = v, action),
            onFinished);
    }

    protected Task RunLocalReloadAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        return _reload.RunAsync(
            ct => RunReloadPipelineAsync(ct, setBusy, action),
            onFinished);
    }

    private async Task RunReloadPipelineAsync(
        CancellationToken ct,
        Action<bool> setLoadingBusy,
        Func<CancellationToken, Task> fetch)
    {
        using var activity = PacActivities.Desktop.StartActivity("page.reload");
        using var traceScope = LogTrace.Begin(activity?.TraceId.ToString());
        var sw = Stopwatch.StartNew();
        LogReloadStarted();

        var requiresLocalDb = RequiresLocalDbForReload;
        IDbConnectionMonitorService? mon = null;
        if (requiresLocalDb)
        {
            _ = GetDbMonitor();
            _ = GetDbAccessGuard();

            if (IsDbAccessBlocked(out var blockReason))
            {
                SetPageAvailability(PageDataAvailability.AccessBlocked, blockReason);
                LogReloadSkipped("access_blocked");
                return;
            }

            if (!IsDbConnected)
            {
                SetPageAvailability(GetDisconnectedAvailability());
            }

            if (!await WaitStartupReadyAsync(ct).ConfigureAwait(false))
            {
                LogReloadSkipped("startup_not_ready");
                return;
            }

            mon = GetDbMonitor();
            var resumedFromWait = false;
            if (mon is not null && !mon.IsConnected)
            {
                SetPageAvailability(GetDisconnectedAvailability());
                var ok = await WaitForConnectedAsync(mon, ct).ConfigureAwait(false);
                if (!ok)
                {
                    LogReloadSkipped("db_wait_failed");
                    return;
                }

                resumedFromWait = true;
            }

            if (IsDbAccessBlocked(out var blockedAfterWait))
            {
                SetPageAvailability(PageDataAvailability.AccessBlocked, blockedAfterWait);
                LogReloadSkipped("access_blocked");
                return;
            }

            if (resumedFromWait)
            {
                try
                {
                    // 重连后稍等再查连接池，避免刚恢复就打到未就绪连接
                    await Task.Delay(ReconnectSettleDelay, GetTime(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    RestoreAfterCancel();
                    LogReloadFinished("cancelled", sw.ElapsedMilliseconds);
                    return;
                }
            }
        }

        var suppressReloadBusy = SuppressReloadBusy();
        if (!suppressReloadBusy)
        {
            SetPageAvailability(PageDataAvailability.Loading);
        }

        try
        {
            // stale-while-reconnect：保留缓存行，不盖 loading 遮罩
            if (suppressReloadBusy)
            {
                await RunWithTransportRetryAsync(fetch, mon, ct).ConfigureAwait(false);
            }
            else
            {
                await PageReloadBusyDelay.RunAsync(
                    ct,
                    setLoadingBusy,
                    () => RunWithTransportRetryAsync(fetch, mon, ct),
                    GetTime()).ConfigureAwait(false);
            }

            MarkHasLoadedOnce();
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.Ready);
            LogReloadFinished("ready", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            RestoreAfterCancel();
            LogReloadFinished("cancelled", sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (RequiresLocalDbForReload && IsDbAccessBlockedException(ex))
        {
            LogWarn("reload.access_blocked.fail", "Reload stopped because database access is blocked", ex);
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.AccessBlocked, GetDbAccessGuard()?.BlockReason);
            LogReloadFinished("access_blocked", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            HandleReloadException(ex);

            if (IsTransportError(ex))
            {
                RestoreAfterFail(ex);
                LogReloadFinished("transport_degraded", sw.ElapsedMilliseconds);
                return;
            }

            var message = ex.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "页面数据加载失败";
            }

            LogError("reload.pipeline.fail", "Page reload failed with non-transport error", ex);
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.LoadFailed, message);
            LogReloadFinished("load_failed", sw.ElapsedMilliseconds);
        }
    }

    private void RestoreAfterCancel()
    {
        if (RequiresLocalDbForReload && IsDbAccessBlocked(out var reason))
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        // 服务降级优先：Loading 被取消时按是否已加载回到 AwaitingService 或 Stale，勿先按库断改态
        if (_pageDataAvailability is PageDataAvailability.AwaitingService
            || HasArmedServiceRetry())
        {
            if (_pageDataAvailability is PageDataAvailability.Loading)
            {
                SetPageAvailability(GetServiceUnavailableAvailability());
            }

            return;
        }

        if (RequiresLocalDbForReload && !IsDbConnected)
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

        ClearServiceRetryDeadline();
        SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
    }

    private void RestoreAfterFail(Exception? ex = null)
    {
        if (RequiresLocalDbForReload && IsDbAccessBlocked(out var reason))
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        // 远端传输失败进服务等待；本机页只有 PacApi 等非库故障才进。先记截止再改可用性，Stale 文案才跟服务降级
        if (!RequiresLocalDbForReload
                ? ex is not null && IsTransportError(ex)
                : IsRemoteTransport(ex))
        {
            ArmServiceRetry(ex);
            SetPageAvailability(GetServiceUnavailableAvailability());
            return;
        }

        // Signal 只排队探测，Connected 可能还没翻；本机 Pg 故障直接按异常进库断态
        if (RequiresLocalDbForReload
            && ex is not null
            && TransportErrors.SignalsDbDisconnect(ex))
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

        if (RequiresLocalDbForReload && !IsDbConnected)
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

        ClearServiceRetryDeadline();
        SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
    }

    private static bool IsRemoteTransport(Exception? ex)
        => ex is not null
           && TransportErrors.IsTransport(ex)
           && !TransportErrors.SignalsDbDisconnect(ex);

    private PageDataAvailability GetDisconnectedAvailability()
        => PageReconnectPolicy.DisconnectedAvailability(_hasLoadedOnce, SupportsStaleWhileReconnect);

    private PageDataAvailability GetServiceUnavailableAvailability()
        => PageReconnectPolicy.ServiceUnavailableAvailability(_hasLoadedOnce, SupportsStaleWhileReconnect);

    private bool SuppressReloadBusy()
        => PageReconnectPolicy.SuppressReloadBusy(
            _hasLoadedOnce,
            SupportsStaleWhileReconnect,
            _reloadFromSignal);

    private async Task<bool> WaitStartupReadyAsync(CancellationToken ct)
    {
        var startup = GetStartupState();
        if (startup is null || startup.IsDbInitCompleted)
        {
            return true;
        }

        return await WaitForStartupDbInitCompletedAsync(startup, ct).ConfigureAwait(false);
    }

    private void MarkHasLoadedOnce()
    {
        if (_hasLoadedOnce)
        {
            return;
        }

        _hasLoadedOnce = true;
        OnPropertyChanged(nameof(HasLoadedOnce));
        OnPropertyChanged(nameof(IsSectionPending));
        OnPageAvailabilityChanged();
    }

    private void SetPageAvailability(PageDataAvailability availability, string? detail = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyPageAvailability(availability, detail);
            return;
        }

        PostOnUi(() => ApplyPageAvailability(availability, detail));
    }

    private void ApplyPageAvailability(PageDataAvailability availability, string? detail = null)
    {
        if (availability == PageDataAvailability.AccessBlocked)
        {
            _accessBlockedReason = detail;
            _loadFailedMessage = null;
        }
        else if (availability == PageDataAvailability.LoadFailed)
        {
            _loadFailedMessage = detail;
            _accessBlockedReason = null;
        }
        else
        {
            _accessBlockedReason = null;
            _loadFailedMessage = null;
        }

        if (_pageDataAvailability == availability
            && availability == PageDataAvailability.AccessBlocked
            && string.Equals(_accessBlockedReason, detail, StringComparison.Ordinal))
        {
            return;
        }

        if (_pageDataAvailability == availability
            && availability == PageDataAvailability.LoadFailed
            && string.Equals(_loadFailedMessage, detail, StringComparison.Ordinal))
        {
            return;
        }

        if (_pageDataAvailability == availability
            && availability is not PageDataAvailability.AccessBlocked
            and not PageDataAvailability.LoadFailed)
        {
            return;
        }

        _pageDataAvailability = availability;
        if (ShowPageUnavailable || availability == PageDataAvailability.Stale)
        {
            // 陈旧数据或不可用状态优先于加载状态，避免同时显示相互冲突的反馈
            IsBusy = false;
        }

        if (availability == PageDataAvailability.AccessBlocked)
        {
            OnLookupCatalogSuspended();
        }

        RefreshPageAvailability();
    }

    protected bool IsLookupCatalogSuspended()
        => IsDbAccessBlocked(out _);

    protected virtual void OnLookupCatalogSuspended()
    {
    }

    protected virtual void OnPageAvailabilityChanged()
    {
    }

    private void RefreshPageAvailability()
    {
        OnPropertyChanged(nameof(ShowPageUnavailable));
        OnPropertyChanged(nameof(PageUnavailableTitle));
        OnPropertyChanged(nameof(PageUnavailableHint));
        OnPropertyChanged(nameof(PageUnavailableIcon));
        OnPropertyChanged(nameof(PageStaleHint));
        OnPropertyChanged(nameof(IsShowingStaleData));
        OnPropertyChanged(nameof(CanPageFromDb));
        OnPropertyChanged(nameof(SectionEmptyIcon));
        OnPropertyChanged(nameof(IsSectionPending));
        OnPageAvailabilityChanged();
    }

    /// <summary>
    /// 只按访问限制与 DB monitor 同步页面可用性，不拉业务数据
    /// </summary>
    public void SyncPageAvailability()
    {
        // 远端页只跟服务重试与重载，不跟本机库连断
        if (!RequiresLocalDbForReload)
        {
            return;
        }

        if (IsDbAccessBlocked(out var reason))
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        // 服务等待或已挂服务截止的 Stale，不要被库连断翻掉
        if (_pageDataAvailability is PageDataAvailability.AwaitingService
            || (_pageDataAvailability is PageDataAvailability.Stale && HasArmedServiceRetry()))
        {
            if (IsDbConnected)
            {
                ResumeServiceRetryIfNeeded();
            }

            return;
        }

        if (!IsDbConnected)
        {
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

        // 库恢复后清掉库断带来的 Stale 与 AwaitingDatabase
        if (_pageDataAvailability is PageDataAvailability.Stale
            or PageDataAvailability.AwaitingDatabase
            or PageDataAvailability.NotLoaded)
        {
            SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
        }
    }

    private async Task RunWithTransportRetryAsync(
        Func<CancellationToken, Task> action,
        IDbConnectionMonitorService? mon,
        CancellationToken ct,
        int maxAttempts = 2)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsTransportError(ex) && attempt < maxAttempts)
            {
                // PacApi 已有 Http Resilience；页面层不再即时重试，交给服务等待与 Retry-After
                if (TransportErrors.TryFindPacApiException(ex, out _))
                {
                    throw;
                }

                LogWarn("reload.transport_retry", "Retrying reload after transport error", ex, new { attempt });
                if (RequiresLocalDbForReload)
                {
                    MarkDbDisconnectedOnTransportError(ex);
                }

                try
                {
                    await Task.Delay(ReconnectSettleDelay, GetTime(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                if (RequiresLocalDbForReload && mon is not null && !mon.IsConnected)
                {
                    var ok = await WaitForConnectedAsync(mon, ct).ConfigureAwait(false);
                    if (!ok)
                    {
                        ExceptionDispatchInfo.Capture(ex).Throw();
                    }
                }
            }
        }
    }

    /// <summary>夹紧 API Retry-After，避免过短叠加重试或过长卡死页面</summary>
    internal static TimeSpan ClampRetryAfter(TimeSpan value)
    {
        if (value < MinRetryAfterDelay)
        {
            return MinRetryAfterDelay;
        }

        if (value > MaxRetryAfterDelay)
        {
            return MaxRetryAfterDelay;
        }

        return value;
    }

    private void ClearServiceRetryDeadline()
    {
        CancellationTokenSource cancel;
        lock (_serviceRetryGate)
        {
            _nextRetryAt = null;
            _scheduledRetryAt = null;
            _serviceRetryQueued = false;
            _serviceRetryGeneration++;
            cancel = _serviceRetryCts;
            _serviceRetryCts = new CancellationTokenSource();
        }

        try
        {
            cancel.Cancel();
        }
        catch (Exception ex)
        {
            LogWarn("reload.service_retry.clear_cancel.fail", "Failed to cancel service retry CTS", ex);
        }

        cancel.Dispose();
    }

    // 写入绝对截止并排程；已有任务且截止变化则取消重排
    private void ArmServiceRetry(Exception? ex)
    {
        if (IsDisposed)
        {
            return;
        }

        var now = GetTime().GetUtcNow();
        DateTimeOffset due;
        if (ex is not null)
        {
            var delay = _serviceRetryDelay;
            if (TransportErrors.TryFindPacApiException(ex, out var api) && api.RetryAfter is { } after)
            {
                delay = ClampRetryAfter(after);
            }

            due = now + delay;
        }
        else
        {
            // 续排/恢复：若截止仍在未来则沿用；已过期则从现在起算，避免 delay<=0 同步死循环
            lock (_serviceRetryGate)
            {
                due = _nextRetryAt is { } pending && pending > now + TimeSpan.FromMilliseconds(1)
                    ? pending
                    : now + _serviceRetryDelay;
            }
        }

        // 未激活或暂不能自动刷新时仍记下截止，免得 Sync 误清 Stale；排程等 Resume
        var canSchedule = CanAutoRefreshFromDbSignal();

        CancellationTokenSource? old = null;
        int generation;
        CancellationToken ct;
        lock (_serviceRetryGate)
        {
            if (IsDisposed)
            {
                return;
            }

            _nextRetryAt = due;

            if (!_serviceRetryAllowed || !canSchedule)
            {
                return;
            }

            if (_serviceRetryQueued
                && _scheduledRetryAt is { } scheduled
                && scheduled == due)
            {
                return;
            }

            if (_serviceRetryQueued)
            {
                _serviceRetryGeneration++;
                old = _serviceRetryCts;
                _serviceRetryCts = new CancellationTokenSource();
            }

            _serviceRetryQueued = true;
            _scheduledRetryAt = due;
            generation = _serviceRetryGeneration;
            ct = _serviceRetryCts.Token;
        }

        if (old is not null)
        {
            try { old.Cancel(); }
            catch { }

            try { old.Dispose(); }
            catch { }
        }

        PostServiceRetry(generation, ct, due);
    }

    protected async Task RunLocalBusyAsync(
        CancellationToken ct,
        Action<bool> setBusy,
        Func<Task> body,
        bool showBusy = true)
    {
        if (!showBusy)
        {
            await body().ConfigureAwait(false);
            return;
        }

        await PageReloadBusyDelay.RunAsync(ct, setBusy, body, GetTime()).ConfigureAwait(false);
    }


    private void HandleReloadException(Exception ex)
    {
        if (RequiresLocalDbForReload && IsDbAccessBlockedException(ex))
        {
            LogWarn("reload.access_blocked.fail", "Reload stopped because database access is blocked", ex);
            return;
        }

        if (IsTransportError(ex))
        {
            if (RequiresLocalDbForReload && MarkDbDisconnectedOnTransportError(ex))
            {
                LogWarn("reload.db_transport_error", "Reload hit local DB transport error, signaling monitor", ex);
            }
            else
            {
                LogWarn("reload.transport_error", "Reload hit transient transport error without signaling DB monitor", ex);
            }
        }
    }

    protected bool IsDbAccessBlocked(out string? reason)
    {
        var guard = GetDbAccessGuard();
        reason = guard?.BlockReason;
        return guard?.IsBlocked == true;
    }

    protected bool IsDbAccessBlockedException(Exception ex)
    {
        if (!IsDbAccessBlocked(out var reason) || string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is InvalidOperationException && string.Equals(cur.Message, reason, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private IDbAccessGuard? GetDbAccessGuard()
    {
        if (_cachedAccessGuard is not null)
        {
            return _cachedAccessGuard;
        }

        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                _cachedAccessGuard = app.Services.GetService(typeof(IDbAccessGuard)) as IDbAccessGuard;
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_access_guard.fail", "Failed to resolve database access guard from DI", ex);
        }

        return _cachedAccessGuard;
    }

    protected bool IsTransportError(Exception ex)
        => TransportErrors.IsTransport(ex);

    protected void SignalDbDisconnected()
    {
        GetDbMonitor()?.Signal();
    }

    protected bool MarkDbDisconnectedOnTransportError(Exception ex)
    {
        // 只有本机 Pg 故障才 Signal；PacApi、MSFX、更新源这类 HTTP 不要动 DB 状态
        if (!TransportErrors.SignalsDbDisconnect(ex))
        {
            return false;
        }

        SignalDbDisconnected();
        return true;
    }

    /// <summary>
    /// 本机 Pg 故障交给 Shell banner；远端页还没有 API banner，瞬时错误才由页面 toast
    /// </summary>
    protected bool CanToastError(Exception ex)
    {
        if (!RequiresLocalDbForReload)
        {
            // 纯远端页不访问本机 Pg；裸 Socket 也按服务错误 toast
            return true;
        }

        if (IsDbAccessBlockedException(ex))
        {
            return false;
        }

        if (TransportErrors.IsTransport(ex))
        {
            MarkDbDisconnectedOnTransportError(ex);
            return false;
        }

        if (GetTime().GetUtcNow() < _reconnectToastSuppressUntil)
        {
            // 主窗口统一报告重新连接结果，页面在冷却期内保持静默
            return false;
        }

        return IsDbConnected;
    }

    private TimeProvider GetTime()
    {
        if (_cachedTime is not null)
        {
            return _cachedTime;
        }

        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                _cachedTime = app.Services.GetService(typeof(TimeProvider)) as TimeProvider;
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_time.fail", "Failed to resolve TimeProvider from DI", ex);
        }

        return _cachedTime ??= TimeProvider.System;
    }

    private IDbConnectionMonitorService? GetDbMonitor()
    {
        if (!RequiresLocalDbForReload)
        {
            return _cachedDbMonitor;
        }

        if (_cachedDbMonitor is not null)
        {
            HookDbMonitor(_cachedDbMonitor);
            return _cachedDbMonitor;
        }

        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                _cachedDbMonitor = app.Services.GetService(typeof(IDbConnectionMonitorService)) as IDbConnectionMonitorService;
                if (_cachedDbMonitor is not null)
                {
                    HookDbMonitor(_cachedDbMonitor);
                }
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_db_monitor.fail", "Failed to resolve DB monitor from DI", ex);
        }

        return _cachedDbMonitor;
    }

    private IAppStartupStateService? GetStartupState()
    {
        if (_cachedStartupState is not null)
        {
            return _cachedStartupState;
        }

        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                _cachedStartupState = app.Services.GetService(typeof(IAppStartupStateService)) as IAppStartupStateService;
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_startup_state.fail", "Failed to resolve startup state from DI", ex);
        }

        return _cachedStartupState;
    }

    private void HookDbMonitor(IDbConnectionMonitorService monitor)
    {
        if (!RequiresLocalDbForReload || _dbMonitorEventsHooked)
        {
            return;
        }

        monitor.Disconnected += OnDbMonitorDisconnected;
        monitor.Reconnected += OnDbMonitorReconnected;
        monitor.Reconnected += StartReconnectToastCooldown;
        _dbMonitorEventsHooked = true;

        // 页面初始化时数据库已断开，应先显示不可用状态并等待自动刷新
        if (!monitor.IsConnected)
        {
            PostOnUi(SyncPageAvailability);
            if (AutoRefreshOnDbDisconnected)
            {
                ScheduleAutoRefreshFromDbSignal();
            }
        }
    }

    private void OnDbMonitorDisconnected()
    {
        if (!RequiresLocalDbForReload)
        {
            return;
        }

        PostOnUi(SyncPageAvailability);

        if (!AutoRefreshOnDbDisconnected)
        {
            return;
        }

        ScheduleAutoRefreshFromDbSignal();
    }

    private void StartReconnectToastCooldown()
    {
        if (!RequiresLocalDbForReload)
        {
            return;
        }

        _reconnectToastSuppressUntil = GetTime().GetUtcNow() + ReconnectToastSuppressWindow;
    }

    private void OnDbMonitorReconnected()
    {
        if (!RequiresLocalDbForReload)
        {
            return;
        }

        PostOnUi(SyncPageAvailability);

        if (!AutoRefreshOnDbReconnected)
        {
            return;
        }

        // 等待数据库的现有重载会在连接恢复后继续，无需重复安排自动刷新
        if (_reload.IsActive)
        {
            return;
        }

        ScheduleAutoRefreshFromDbSignal();
    }

    private void ScheduleAutoRefreshFromDbSignal()
    {
        // 合并连续的数据库断开与恢复信号，只触发一次自动刷新
        if (Interlocked.Exchange(ref _dbSignalRefreshQueued, 1) == 1)
        {
            return;
        }

        PostOnUi(() => ObserveDetached(AutoRefreshOnDbSignalAsync(), "auto_refresh.detached.fail"), DispatcherPriority.Background);
    }

    private void ResumeServiceRetryIfNeeded()
    {
        if (IsDisposed)
        {
            return;
        }

        if (_pageDataAvailability is PageDataAvailability.AwaitingService)
        {
            ArmServiceRetry(ex: null);
            return;
        }

        if (_pageDataAvailability is not PageDataAvailability.Stale)
        {
            return;
        }

        // 远端 Stale 一律续排；本机页只续排已挂服务截止的（库断 Stale 等 reconnect）
        if (!RequiresLocalDbForReload || HasArmedServiceRetry())
        {
            ArmServiceRetry(ex: null);
        }
    }

    private bool HasArmedServiceRetry()
    {
        lock (_serviceRetryGate)
        {
            return _nextRetryAt is not null;
        }
    }

    private void CancelServiceRetry()
    {
        CancellationTokenSource old;
        lock (_serviceRetryGate)
        {
            _serviceRetryGeneration++;
            _serviceRetryQueued = false;
            _serviceRetryAllowed = false;
            _scheduledRetryAt = null;
            old = _serviceRetryCts;
            _serviceRetryCts = new CancellationTokenSource();
        }

        try
        {
            old.Cancel();
        }
        catch
        {
        }

        try
        {
            old.Dispose();
        }
        catch
        {
        }
    }

    private void PostServiceRetry(int generation, CancellationToken ct, DateTimeOffset due)
    {
        PostOnUi(
            () => ObserveDetached(
                AutoRefreshOnServiceRetryAsync(ct, generation, due),
                "auto_refresh.service_retry.fail"),
            DispatcherPriority.Background);
    }

    private async Task AutoRefreshOnServiceRetryAsync(
        CancellationToken ct,
        int generation,
        DateTimeOffset due)
    {
        try
        {
            var delay = due - GetTime().GetUtcNow();
            if (delay < TimeSpan.FromMilliseconds(1))
            {
                // PostOnUi 同步执行时 delay<=0 会紧循环撑爆栈；让出一拍再按默认间隔重排
                await Task.Yield();
                delay = _serviceRetryDelay;
            }

            await Task.Delay(delay, GetTime(), ct).ConfigureAwait(false);

            bool stillCurrent;
            lock (_serviceRetryGate)
            {
                stillCurrent = _serviceRetryAllowed && _serviceRetryGeneration == generation;
                if (stillCurrent)
                {
                    // 开火只清排程位，截止留给 Sync 识别服务 Stale；成功再 Clear，失败由 Arm 改写
                    _scheduledRetryAt = null;
                }
            }

            if (IsDisposed
                || !stillCurrent
                || _pageDataAvailability is not (PageDataAvailability.AwaitingService or PageDataAvailability.Stale))
            {
                return;
            }

            _reloadFromSignal = true;
            try
            {
                var refresh = RefreshCommand;
                if (refresh is null || !refresh.CanExecute(null))
                {
                    return;
                }

                if (refresh is IAsyncRelayCommand asyncRefresh)
                {
                    await asyncRefresh.ExecuteAsync(null).ConfigureAwait(false);
                }
                else
                {
                    refresh.Execute(null);
                }
            }
            finally
            {
                _reloadFromSignal = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogWarn("reload.service_retry.fail", "Auto refresh after service wait failed", ex);
        }
        finally
        {
            var reschedule = false;
            lock (_serviceRetryGate)
            {
                if (_serviceRetryGeneration == generation)
                {
                    _serviceRetryQueued = false;
                    _scheduledRetryAt = null;
                    // 本机库仍断时不要空转续排；库断 Stale 等 monitor
                    reschedule = _serviceRetryAllowed
                        && !IsDisposed
                        && !ct.IsCancellationRequested
                        && (_pageDataAvailability is PageDataAvailability.AwaitingService
                            || (_pageDataAvailability is PageDataAvailability.Stale
                                && (!RequiresLocalDbForReload || IsDbConnected)));
                }
            }

            if (reschedule)
            {
                // 可用性未变时续排；失败路径若已 Arm 会换 generation，这里不会双排
                ArmServiceRetry(ex: null);
            }
        }
    }

    private async Task AutoRefreshOnDbSignalAsync()
    {
        _reloadFromSignal = true;
        try
        {
            if (!CanAutoRefreshFromDbSignal())
            {
                return;
            }

            var refresh = RefreshCommand;
            if (refresh is null)
            {
                return;
            }

            if (!refresh.CanExecute(null))
            {
                return;
            }

            if (refresh is IAsyncRelayCommand asyncRefresh)
            {
                await asyncRefresh.ExecuteAsync(null).ConfigureAwait(false);
            }
            else
            {
                refresh.Execute(null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.db_signal_refresh.fail", "Auto refresh from DB signal failed", ex);
        }
        finally
        {
            _reloadFromSignal = false;
            Interlocked.Exchange(ref _dbSignalRefreshQueued, 0);
        }
    }

    private static async Task<bool> WaitForConnectedAsync(IDbConnectionMonitorService mon, CancellationToken ct)
    {
        if (mon.IsConnected)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReconnected() => tcs.TrySetResult(true);

        mon.Reconnected += OnReconnected;

        try
        {
            if (mon.IsConnected)
            {
                return true;
            }

            await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
            return mon.IsConnected;
        }
        catch (OperationCanceledException)
        {
            return mon.IsConnected;
        }
        finally
        {
            mon.Reconnected -= OnReconnected;
        }
    }

    private static async Task<bool> WaitForStartupDbInitCompletedAsync(IAppStartupStateService startup, CancellationToken ct)
    {
        if (startup.IsDbInitCompleted)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted() => tcs.TrySetResult(true);

        startup.DbInitCompleted += OnCompleted;

        try
        {
            if (startup.IsDbInitCompleted)
            {
                return true;
            }

            await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
            return startup.IsDbInitCompleted;
        }
        catch (OperationCanceledException)
        {
            return startup.IsDbInitCompleted;
        }
        finally
        {
            startup.DbInitCompleted -= OnCompleted;
        }
    }

    private int _disposeGate;

    // 同一 Page 以 TPage 与 AppPageBase 各注册一次（供具体注入与 IEnumerable）；MS DI 会对同一实例 Dispose 两次，故门闩保证幂等
    protected bool IsDisposed => Volatile.Read(ref _disposeGate) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
        CancelServiceRetry();
        try
        {
            _serviceRetryCts.Dispose();
        }
        catch
        {
        }

        if (_cachedDbMonitor is not null && _dbMonitorEventsHooked)
        {
            try
            {
                _cachedDbMonitor.Disconnected -= OnDbMonitorDisconnected;
                _cachedDbMonitor.Reconnected -= OnDbMonitorReconnected;
                _cachedDbMonitor.Reconnected -= StartReconnectToastCooldown;
            }
            catch (System.Exception ex)
            {
                LogWarn("reload.db_monitor_unhook.fail", "Failed to unhook DB monitor events", ex);
            }
        }

        _reload.Dispose();
        _dbMonitorEventsHooked = false;
        _cachedDbMonitor = null;
        _cachedStartupState = null;
        _cachedAccessGuard = null;
    }
}
