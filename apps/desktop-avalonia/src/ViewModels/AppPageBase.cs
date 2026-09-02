using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Diagnostics;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Connectivity;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;
using PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;
using PacToolkits.Desktop.Avalonia.Ui.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// 页面重载、数据可用性与生命周期；业务查询和 DataGrid 行模型由派生页负责
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

    protected virtual bool SupportsStaleWhileReconnect => true;

    private readonly PageReload _reload = new();
    private readonly ConcurrentDictionary<string, byte> _uiCoalesceGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan DefaultServiceRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinRetryAfterDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryAfterDelay = TimeSpan.FromSeconds(60);
    private TimeSpan _serviceRetryDelay = DefaultServiceRetryDelay;
    // 绝对截止（TimeProvider）；每次终态失败替换，成功清空
    private DateTimeOffset? _nextRetryAt;
    private DateTimeOffset? _scheduledRetryAt;
    private TimeProvider? _cachedTime;
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
    private ApiAvailabilitySnapshot? _apiSnap;
    private IApiAvailabilityService? _cachedApiAvailability;

    /// <summary>工作区脏刷、服务续排、库信号等自动重载</summary>
    protected bool IsSignalReload => _reloadFromSignal;

    /// <summary>自动重载：不盖 Busy</summary>
    public IDisposable BeginSilentReload()
    {
        var previous = _reloadFromSignal;
        _reloadFromSignal = true;
        return new FlagRestore(() => _reloadFromSignal = previous);
    }

    private sealed class FlagRestore(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _restore, null);
            action?.Invoke();
        }
    }

    protected bool IsPageReloadActive => _reload.IsActive;

    public bool HasLoadedOnce => _hasLoadedOnce;

    public PageDataAvailability PageDataAvailability => _pageDataAvailability;

    public bool IsShowingStaleData => _pageDataAvailability == PageDataAvailability.Stale;

    public bool CanPage => IsPageConnected && !IsPageBlocked(out _);

    public bool ShowPageUnavailable => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => true,
        PageDataAvailability.LoadFailed => true,
        _ => false
    };

    public string PageUnavailableTitle => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => ConnectionView.From(CurrentApiSnap, IsApiConfigured).Title is { Length: > 0 } title
            ? title
            : "PacAPI 服务不可用",
        PageDataAvailability.LoadFailed => "加载失败",
        PageDataAvailability.AwaitingService => "PacAPI 服务暂不可用",
        PageDataAvailability.NotLoaded => "PacAPI 服务暂不可用",
        _ => string.Empty
    };

    public string? PageUnavailableHint => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => string.IsNullOrWhiteSpace(_accessBlockedReason)
            ? "恢复后将自动加载"
            : _accessBlockedReason,
        PageDataAvailability.LoadFailed => _loadFailedMessage ?? "请稍后重试",
        PageDataAvailability.AwaitingService => "恢复后将自动加载",
        PageDataAvailability.NotLoaded => "恢复后将自动加载",
        _ => null
    };

    public string PageUnavailableIcon => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => "ShieldAlert",
        PageDataAvailability.LoadFailed => "CircleAlert",
        PageDataAvailability.AwaitingService => "Server",
        PageDataAvailability.NotLoaded => "Server",
        _ => "Server"
    };

    public string SectionEmptyIcon => SectionEmptyCopy.GetIcon(_pageDataAvailability);

    public bool IsSectionPending => SectionEmptyPolicy.IsPending(
        _hasLoadedOnce,
        _pageDataAvailability,
        ConnectionView.From(CurrentApiSnap, IsApiConfigured).Kind);

    protected string GetSectionEmptyTitle(string? readyTitle)
        => SectionEmptyCopy.GetTitle(readyTitle);

    protected string GetSectionEmptyHint(string? readyHint)
        => SectionEmptyCopy.GetHint(
            _pageDataAvailability,
            readyHint,
            _accessBlockedReason,
            _loadFailedMessage);

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

    private bool IsPageConnected => IsApiReady;

    private bool IsApiConfigured => GetApiAvailability()?.IsConfigured ?? true;

    private bool IsPageBlocked(out string? reason)
    {
        if (!ConnectionView.IsBlocked(CurrentApiSnap, IsApiConfigured))
        {
            reason = null;
            return false;
        }

        var view = ConnectionView.From(CurrentApiSnap, IsApiConfigured);
        reason = string.IsNullOrWhiteSpace(view.Message) ? view.Title : view.Message;
        return true;
    }

    protected bool ShowSectionEmpty(bool isContentEmpty)
        => isContentEmpty && !IsSectionPending;

    protected AppPageBase()
    {
        _refreshCommand = new AsyncRelayCommand(
            execute: ExecuteRefreshAsync,
            canExecute: CanRefresh);

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

    private bool CanRefresh() => IsEnabled && CanPage;

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

    /// <summary>与重载共用 PageReload 互斥，但不进入页面 Loading</summary>
    protected Task RunExclusiveLocalBusyAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> body,
        Action? onFinished = null)
    {
        return _reload.RunAsync(
            ct => RunLocalBusyAsync(ct, setBusy, () => body(ct)),
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

        if (!IsPageConnected || IsPageBlocked(out _))
        {
            SyncPageAvailability();
            LogReloadSkipped("service_unavailable");
            return;
        }

        var suppressReloadBusy = _reloadFromSignal;
        if (!suppressReloadBusy)
        {
            SetPageAvailability(PageDataAvailability.Loading);
        }

        try
        {
            // stale-while-reconnect：保留缓存行，不盖 loading 遮罩
            if (suppressReloadBusy)
            {
                await fetch(ct).ConfigureAwait(false);
            }
            else
            {
                await PageReloadBusyDelay.RunAsync(
                    ct,
                    setLoadingBusy,
                    () => fetch(ct),
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
        // Loading 取消时若退避已 Arm，回到 AwaitingService / Stale，勿清截止
        if (_pageDataAvailability is PageDataAvailability.AwaitingService
            || HasArmedServiceRetry())
        {
            if (_pageDataAvailability is PageDataAvailability.Loading)
            {
                SetPageAvailability(GetServiceUnavailableAvailability());
            }

            return;
        }

        RestoreFromConnection();
    }

    private void RestoreAfterFail(Exception? ex = null)
    {
        if (IsPageBlocked(out var reason))
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        // 探测仍为 Up 时由页面安排重试；Down 时等待 Shell 通知恢复
        if (ex is not null && IsTransportError(ex))
        {
            if (IsPageConnected)
            {
                ArmServiceRetry(ex);
            }

            SetPageAvailability(GetServiceUnavailableAvailability());
            return;
        }

        RestoreFromConnection();
    }

    private void RestoreFromConnection()
    {
        if (IsPageBlocked(out var reason))
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        if (!IsPageConnected)
        {
            ClearServiceRetryDeadline();
            SetPageAvailability(GetServiceUnavailableAvailability());
            return;
        }

        ClearServiceRetryDeadline();
        SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
    }

    private PageDataAvailability GetServiceUnavailableAvailability()
        => PageReconnectPolicy.ServiceUnavailableAvailability(_hasLoadedOnce, SupportsStaleWhileReconnect);

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
        var previous = _pageDataAvailability;
        var previousBlocked = _accessBlockedReason;
        var previousFailed = _loadFailedMessage;

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

        if (previous == availability
            && availability == PageDataAvailability.AccessBlocked
            && string.Equals(previousBlocked, detail, StringComparison.Ordinal))
        {
            return;
        }

        if (previous == availability
            && availability == PageDataAvailability.LoadFailed
            && string.Equals(previousFailed, detail, StringComparison.Ordinal))
        {
            return;
        }

        if (previous == availability
            && availability is not PageDataAvailability.AccessBlocked
            and not PageDataAvailability.LoadFailed)
        {
            return;
        }

        _pageDataAvailability = availability;
        if (availability is PageDataAvailability.Stale
            or PageDataAvailability.AwaitingService
            or PageDataAvailability.AccessBlocked
            or PageDataAvailability.LoadFailed
            or PageDataAvailability.NotLoaded)
        {
            // 等待 / 陈旧 / 阻断 / 失败时 Busy 必须为假；Ready/Loading 由拉数路径自己管
            IsBusy = false;
        }

        if (availability == PageDataAvailability.AccessBlocked)
        {
            OnLookupCatalogSuspended();
        }

        RefreshPageAvailability();
        _refreshCommand.NotifyCanExecuteChanged();
    }

    protected bool IsLookupCatalogSuspended()
        => false;

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
        OnPropertyChanged(nameof(IsShowingStaleData));
        OnPropertyChanged(nameof(CanPage));
        OnPropertyChanged(nameof(SectionEmptyIcon));
        OnPropertyChanged(nameof(IsSectionPending));
        OnPageAvailabilityChanged();
    }

    /// <summary>
    /// 按访问限制与连接同步页面可用性，不拉业务数据
    /// 业务页通过 ConnectionView 区分可用、不可用与 AccessBlocked
    /// </summary>
    public void SyncPageAvailability()
    {
        if (IsPageBlocked(out var reason))
        {
            ClearServiceRetryDeadline();
            CancelPendingReload();

            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        if (ConnectionView.From(CurrentApiSnap, IsApiConfigured).Kind == ConnectionKind.Unknown)
        {
            ClearServiceRetryDeadline();
            CancelPendingReload();
            SetPageAvailability(_hasLoadedOnce && SupportsStaleWhileReconnect
                ? PageDataAvailability.Stale
                : PageDataAvailability.NotLoaded);
            return;
        }

        // 探测仍 Up 时的自排退避：不要把 AwaitingService / Stale 当成已恢复
        if (IsPageConnected
            && HasArmedServiceRetry()
            && _pageDataAvailability is PageDataAvailability.AwaitingService
                or PageDataAvailability.Stale)
        {
            ResumeServiceRetryIfNeeded();
            return;
        }

        if (!IsPageConnected)
        {
            ClearServiceRetryDeadline();
            CancelPendingReload();

            SetPageAvailability(GetServiceUnavailableAvailability());
            return;
        }

        if (_pageDataAvailability is PageDataAvailability.Stale
            or PageDataAvailability.AwaitingService
            or PageDataAvailability.NotLoaded
            or PageDataAvailability.AccessBlocked)
        {
            SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
        }
    }

    /// <summary>业务页写入探测快照后走 <see cref="SyncPageAvailability"/></summary>
    public void SyncConnection(ApiAvailabilitySnapshot snap)
    {
        var previousAvailability = _pageDataAvailability;
        var previousCanPage = CanPage;
        _apiSnap = snap;
        SyncPageAvailability();

        // 连接就绪前后都可能是 NotLoaded，CanPage 仍需更新
        if (previousAvailability == _pageDataAvailability && previousCanPage != CanPage)
        {
            RefreshPageAvailability();
        }
    }

    /// <summary>收紧 API Retry-After，避免过短叠加重试或过长卡死页面</summary>
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
        var canSchedule = IsEnabled && RefreshCommand is not null;

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
        if (IsTransportError(ex))
        {
            LogWarn("reload.transport_error", "Reload hit transient transport error", ex);
        }
    }

    protected bool IsTransportError(Exception ex)
        => TransportErrors.IsTransport(ex);

    /// <summary>
    /// 连接失败不 toast（横幅 / 探测图标负责）；用户操作仅在服务可用且非传输错误时提示
    /// </summary>
    protected bool CanToastError(Exception ex)
    {
        if (IsTransportError(ex))
        {
            return false;
        }

        return IsApiReady;
    }

    private ApiAvailabilitySnapshot CurrentApiSnap
        => _apiSnap ?? GetApiAvailability()?.Current ?? new ApiAvailabilitySnapshot(
            ApiAvailabilityState.Connecting,
            Detail: null,
            CheckedAt: DateTimeOffset.MinValue,
            FirstCheckCompleted: false);

    protected bool IsApiReady => ConnectionView.IsReady(CurrentApiSnap, IsApiConfigured);

    private IApiAvailabilityService? GetApiAvailability()
    {
        if (_cachedApiAvailability is not null)
        {
            return _cachedApiAvailability;
        }

        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                _cachedApiAvailability = app.Services.GetService(typeof(IApiAvailabilityService)) as IApiAvailabilityService;
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_api_availability.fail", "Failed to resolve API availability from DI", ex);
        }

        return _cachedApiAvailability;
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

    private void ResumeServiceRetryIfNeeded()
    {
        if (IsDisposed)
        {
            return;
        }

        // 业务页仅续排已 Arm 的退避；Down 未 Arm 的等 Shell
        if (!HasArmedServiceRetry())
        {
            return;
        }

        if (_pageDataAvailability is PageDataAvailability.AwaitingService)
        {
            ArmServiceRetry(ex: null);
            return;
        }

        if (_pageDataAvailability is PageDataAvailability.Stale && HasArmedServiceRetry())
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
                    reschedule = _serviceRetryAllowed
                        && !IsDisposed
                        && !ct.IsCancellationRequested
                        && (_pageDataAvailability is PageDataAvailability.AwaitingService
                            or PageDataAvailability.Stale);
                }
            }

            if (reschedule)
            {
                // 可用性未变时续排；失败路径若已 Arm 会换 generation，这里不会双排
                ArmServiceRetry(ex: null);
            }
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

        _reload.Dispose();
    }
}
