using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

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

    private readonly PageReload _reload = new();
    private readonly ConcurrentDictionary<string, byte> _uiCoalesceGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan ReconnectSettleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReconnectToastSuppressWindow = TimeSpan.FromSeconds(5);
    private DateTimeOffset _reconnectToastSuppressUntil = DateTimeOffset.MinValue;

    private IDbConnectionMonitorService? _cachedDbMonitor;
    private IAppStartupStateService? _cachedStartupState;
    private IDbAccessGuard? _cachedAccessGuard;
    private bool _dbMonitorEventsHooked;
    private int _dbSignalRefreshQueued;

    private PageDataAvailability _pageDataAvailability = PageDataAvailability.NotLoaded;
    private string? _accessBlockedReason;
    private string? _loadFailedMessage;
    private bool _hasLoadedOnce;
    private bool _isBusy;
    private bool _reloadFromDbSignal;

    /// <summary>True while the active reload was scheduled from a DB connect/disconnect signal.</summary>
    protected bool IsDbSignalReload => _reloadFromDbSignal;

    public bool HasLoadedOnce => _hasLoadedOnce;

    public PageDataAvailability PageDataAvailability => _pageDataAvailability;

    public bool IsShowingStaleData => _pageDataAvailability == PageDataAvailability.Stale;

    public string PageStaleHint => SectionEmptyCopy.StaleHint;

    public bool ShowPageUnavailable => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => true,
        PageDataAvailability.LoadFailed => true,
        PageDataAvailability.AwaitingDatabase => !_hasLoadedOnce,
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
        PageDataAvailability.NotLoaded => "等待数据库连接",
        _ => string.Empty
    };

    public string? PageUnavailableHint => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => "请前往设置检查数据库版本与迁移状态",
        PageDataAvailability.LoadFailed => _loadFailedMessage ?? "请稍后重试，或使用顶部菜单刷新",
        PageDataAvailability.AwaitingDatabase => "连接恢复后将自动加载",
        PageDataAvailability.NotLoaded => "连接恢复后将自动加载",
        _ => null
    };

    public string PageUnavailableIcon => _pageDataAvailability switch
    {
        PageDataAvailability.AccessBlocked => "ShieldAlert",
        PageDataAvailability.LoadFailed => "CircleAlert",
        _ => "Database"
    };

    public string SectionEmptyIcon => SectionEmptyCopy.GetIcon(_pageDataAvailability);

    public bool IsSectionPending => SectionPendingPolicy.Show(_pageDataAvailability, _hasLoadedOnce);

    protected string GetSectionEmptyTitle(string? readyTitle)
        => SectionEmptyCopy.GetTitle(readyTitle);

    protected string GetSectionEmptyHint(string? readyHint)
        => SectionEmptyCopy.GetHint(
            _pageDataAvailability,
            readyHint,
            _accessBlockedReason,
            _loadFailedMessage);

    /// <summary>
    /// True only while fetching data — not while waiting for DB connectivity.
    /// </summary>
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

        // Eagerly resolve DB monitor on UI thread so disconnect/reconnect signals
        // are not missed before first manual reload.
        PostOnUi(() =>
        {
            _ = GetDbMonitor();
            _ = GetStartupState();
        }, DispatcherPriority.Background);
    }

    protected virtual Task ReloadCoreAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual void OnReloadFinished() { }

    public virtual Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        SyncPageAvailability();
        return Task.CompletedTask;
    }

    public virtual Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelPendingReload();
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
        _ = GetDbMonitor();
        _ = GetDbAccessGuard();

        if (IsDbAccessBlocked(out var blockReason))
        {
            SetPageAvailability(PageDataAvailability.AccessBlocked, blockReason);
            return;
        }

        if (!IsDbConnected)
        {
            SetPageAvailability(GetDisconnectedAvailability());
        }

        if (!await WaitStartupReadyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var mon = GetDbMonitor();
        var resumedFromWait = false;
        if (mon is not null && !mon.IsConnected)
        {
            SetPageAvailability(GetDisconnectedAvailability());
            var ok = await WaitForConnectedAsync(mon, ct).ConfigureAwait(false);
            if (!ok)
            {
                return;
            }

            resumedFromWait = true;
        }

        if (IsDbAccessBlocked(out blockReason))
        {
            SetPageAvailability(PageDataAvailability.AccessBlocked, blockReason);
            return;
        }

        if (resumedFromWait)
        {
            try
            {
                await Task.Delay(ReconnectSettleDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var suppressReloadBusy = SuppressReloadBusy();
        if (!suppressReloadBusy)
        {
            SetPageAvailability(PageDataAvailability.Loading);
        }

        try
        {
            if (suppressReloadBusy)
            {
                await RunWithTransportRetryAsync(fetch, mon, ct).ConfigureAwait(false);
            }
            else
            {
                await PageReloadBusyDelay.RunAsync(
                    ct,
                    setLoadingBusy,
                    () => RunWithTransportRetryAsync(fetch, mon, ct)).ConfigureAwait(false);
            }

            MarkHasLoadedOnce();
            SetPageAvailability(PageDataAvailability.Ready);
        }
        catch (OperationCanceledException)
        {
            RestoreAfterCancel();
        }
        catch (Exception ex) when (IsDbAccessBlockedException(ex))
        {
            LogWarn("reload.access_blocked.fail", "Reload stopped because database access is blocked", ex);
            SetPageAvailability(PageDataAvailability.AccessBlocked, GetDbAccessGuard()?.BlockReason);
        }
        catch (Exception ex)
        {
            HandleReloadException(ex);

            if (IsDbTransportError(ex) || IsDbAccessBlockedException(ex))
            {
                RestoreAfterFail();
                return;
            }

            var message = ex.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "页面数据加载失败";
            }

            LogError("reload.pipeline.fail", "Page reload failed with non-transport error", ex);
            SetPageAvailability(PageDataAvailability.LoadFailed, message);
        }
    }

    private void RestoreAfterCancel()
    {
        if (IsDbAccessBlocked(out var reason))
        {
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        if (!IsDbConnected)
        {
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

        SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
    }

    private void RestoreAfterFail()
    {
        if (IsDbAccessBlocked(out var reason))
        {
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        if (!IsDbConnected)
        {
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

        SetPageAvailability(_hasLoadedOnce ? PageDataAvailability.Ready : PageDataAvailability.NotLoaded);
    }

    private PageDataAvailability GetDisconnectedAvailability()
        => PageReconnectPolicy.DisconnectedAvailability(_hasLoadedOnce, SupportsStaleWhileReconnect);

    private bool SuppressReloadBusy()
        => PageReconnectPolicy.SuppressReloadBusy(
            _hasLoadedOnce,
            SupportsStaleWhileReconnect,
            _reloadFromDbSignal);

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
        OnPropertyChanged(nameof(IsShowingStaleData));
        OnPropertyChanged(nameof(SectionEmptyIcon));
        OnPropertyChanged(nameof(IsSectionPending));
        OnPageAvailabilityChanged();
    }

    /// <summary>
    /// Reconcile page availability with current guard and DB monitor without fetching data.
    /// </summary>
    public void SyncPageAvailability()
    {
        if (IsDbAccessBlocked(out var reason))
        {
            SetPageAvailability(PageDataAvailability.AccessBlocked, reason);
            return;
        }

        if (!IsDbConnected)
        {
            SetPageAvailability(GetDisconnectedAvailability());
            return;
        }

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
            catch (Exception ex) when (IsDbTransportError(ex) && attempt < maxAttempts)
            {
                LogWarn("reload.transport_retry", "Retrying reload after transport error", ex, new { attempt });
                MarkDbDisconnectedOnTransportError(ex);

                try
                {
                    await Task.Delay(ReconnectSettleDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                if (mon is not null && !mon.IsConnected)
                {
                    var ok = await WaitForConnectedAsync(mon, ct).ConfigureAwait(false);
                    if (!ok)
                    {
                        return;
                    }
                }
            }
        }
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

        await PageReloadBusyDelay.RunAsync(ct, setBusy, body).ConfigureAwait(false);
    }


    private void HandleReloadException(Exception ex)
    {
        if (IsDbAccessBlockedException(ex))
        {
            LogWarn("reload.access_blocked.fail", "Reload stopped because database access is blocked", ex);
            return;
        }

        if (IsDbTransportError(ex))
        {
            LogWarn("reload.db_transport_error", "Reload hit transport error, signaling monitor", ex);
            GetDbMonitor()?.Signal();
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

    protected bool IsDbTransportError(Exception ex)
        => DbTransportErrorClassifier.IsTransportError(ex);

    protected void SignalDbDisconnected()
    {
        GetDbMonitor()?.Signal();
    }

    protected bool MarkDbDisconnectedOnTransportError(Exception ex)
    {
        if (!IsDbTransportError(ex))
        {
            return false;
        }

        SignalDbDisconnected();
        return true;
    }

    /// <summary>
    /// Page-level operation errors should not toast when DB transport failed or DB is disconnected;
    /// MainWindow owns the consolidated connection failure/recovery toasts.
    /// </summary>
    protected bool CanToastError(Exception ex)
    {
        if (IsDbAccessBlockedException(ex))
        {
            return false;
        }

        if (DbTransportErrorClassifier.IsTransportError(ex))
        {
            MarkDbDisconnectedOnTransportError(ex);
            return false;
        }

        if (DateTimeOffset.UtcNow < _reconnectToastSuppressUntil)
        {
            return false;
        }

        return IsDbConnected;
    }

    private IDbConnectionMonitorService? GetDbMonitor()
    {
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
        if (_dbMonitorEventsHooked)
        {
            return;
        }

        monitor.Disconnected += OnDbMonitorDisconnected;
        monitor.Reconnected += OnDbMonitorReconnected;
        monitor.Reconnected += StartReconnectToastCooldown;
        _dbMonitorEventsHooked = true;

        // If page initializes while DB is already disconnected, show unavailable and queue refresh.
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
        PostOnUi(SyncPageAvailability);

        if (!AutoRefreshOnDbDisconnected)
        {
            return;
        }

        ScheduleAutoRefreshFromDbSignal();
    }

    private void StartReconnectToastCooldown()
        => _reconnectToastSuppressUntil = DateTimeOffset.UtcNow + ReconnectToastSuppressWindow;

    private void OnDbMonitorReconnected()
    {
        PostOnUi(SyncPageAvailability);

        if (!AutoRefreshOnDbReconnected)
        {
            return;
        }

        // A reload already waiting on DB will resume on reconnect — avoid queuing a duplicate.
        if (_reload.IsActive)
        {
            return;
        }

        ScheduleAutoRefreshFromDbSignal();
    }

    private void ScheduleAutoRefreshFromDbSignal()
    {
        if (Interlocked.Exchange(ref _dbSignalRefreshQueued, 1) == 1)
        {
            return;
        }

        PostOnUi(() => _ = AutoRefreshOnDbSignalAsync(), DispatcherPriority.Background);
    }

    private async Task AutoRefreshOnDbSignalAsync()
    {
        _reloadFromDbSignal = true;
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
            _reloadFromDbSignal = false;
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

    public virtual void Dispose()
    {
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
