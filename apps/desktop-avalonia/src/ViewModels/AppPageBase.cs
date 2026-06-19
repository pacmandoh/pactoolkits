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
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

[StateRetained]
[LongLived]
[OwnsSubscriptions]
public abstract class AppPageBase : ViewModelBase, ITopBarActions, IPageLifecycleAware, IDisposable
{
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
    public abstract int Index { get; }
    public virtual int? BadgeCount => null;
    public virtual bool IsEnabled => true;
    public virtual bool ShowInSidebar => true;

    private readonly IAsyncRelayCommand _refreshCommand;

    public virtual ICommand? RefreshCommand => _refreshCommand;
    public virtual ICommand? ImportCommand => null;
    public virtual ICommand? ExportCommand => null;

    public virtual string? RefreshTip => null;
    public virtual string? ImportTip => null;
    public virtual string? ExportTip => null;
    protected virtual bool AutoRefreshOnDbDisconnected => false;
    protected virtual bool AutoRefreshOnDbReconnected => false;
    protected virtual bool CanAutoRefreshFromDbSignal() => IsEnabled && RefreshCommand is not null;

    private readonly PageReloadBehavior _reload = new();
    private readonly ConcurrentDictionary<string, byte> _uiCoalesceGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan ReconnectSettleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReconnectToastSuppressWindow = TimeSpan.FromSeconds(5);
    private DateTimeOffset _reconnectToastSuppressUntil = DateTimeOffset.MinValue;

    private IDbConnectionMonitorService? _cachedDbMonitor;
    private IAppStartupStateService? _cachedStartupState;
    private bool _dbMonitorEventsHooked;
    private int _dbSignalRefreshQueued;

    private bool _isBusy;

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

    protected bool IsDbConnected => _cachedDbMonitor?.IsConnected ?? true;

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

    public virtual Task OnPageActivatedAsync(CancellationToken ct = default) => Task.CompletedTask;

    public virtual Task OnPageDeactivatedAsync(CancellationToken ct = default) => Task.CompletedTask;

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

    protected static void NotifyCommands(params IRelayCommand?[] commands)
    {
        foreach (var command in commands)
        {
            command?.NotifyCanExecuteChanged();
        }
    }

    protected static Task RunOnUiAsync(Action action)
        => UiThreadHelper.RunOnUiAsync(action, DispatcherPriority.Background);

    protected static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
        => await UiThreadHelper.RunOnUiAsync(action, priority);

    protected static void PostOnUi(Action action)
        => UiThreadHelper.PostOnUi(action, DispatcherPriority.Background);

    protected static void PostOnUi(Action action, DispatcherPriority priority)
        => UiThreadHelper.PostOnUi(action, priority);

    protected void NotifyCommandsCoalesced(string gateKey, Action notifyAction)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            notifyAction();
            return;
        }

        if (!_uiCoalesceGates.TryAdd(gateKey, 0))
        {
            return;
        }

        PostOnUi(() =>
        {
            _uiCoalesceGates.TryRemove(gateKey, out _);
            notifyAction();
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
            setBusy: v => IsBusy = v,
            action: ct => ExecuteReloadActionAsync(action, ct),
            onFinished: onFinished);
    }

    protected Task RunLocalReloadAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        return _reload.RunAsync(
            setBusy: setBusy,
            action: ct => ExecuteReloadActionAsync(action, ct),
            onFinished: onFinished);
    }

    private async Task ExecuteReloadActionAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        var startup = GetStartupState();
        if (startup is not null && !startup.IsDbInitCompleted)
        {
            var ready = await WaitForStartupDbInitCompletedAsync(startup, ct).ConfigureAwait(false);
            if (!ready)
            {
                return;
            }
        }

        var mon = GetDbMonitor();
        var resumedFromWait = false;
        if (mon is not null && !mon.IsConnected)
        {
            var ok = await WaitForConnectedAsync(mon, ct).ConfigureAwait(false);
            if (!ok)
            {
                return;
            }

            resumedFromWait = true;
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

        await RunWithTransportRetryAsync(action, mon, ct).ConfigureAwait(false);
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

        using var busyDelayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var busyDelayTask = Task.Delay(TimeSpan.FromMilliseconds(300), busyDelayCts.Token).ContinueWith(async _ =>
        {
            try
            {
                if (busyDelayCts.IsCancellationRequested)
                {
                    return;
                }

                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => setBusy(true),
                    global::Avalonia.Threading.DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();

        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            busyDelayCts.Cancel();
            try
            {
                await busyDelayTask.ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                LogWarn("reload.busy_delay.fail", "Busy delay task failed", ex);
            }

            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => setBusy(false),
                global::Avalonia.Threading.DispatcherPriority.Background);
        }
    }


    private void HandleReloadException(Exception ex)
    {
        if (IsDbTransportError(ex))
        {
            LogWarn("reload.db_transport_error", "Reload hit transport error, signaling monitor", ex);
            GetDbMonitor()?.Signal();
        }

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
    /// MainWindowViewModel owns the consolidated connection failure/recovery toasts.
    /// </summary>
    protected bool ShouldShowOperationErrorToast(Exception ex)
    {
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
            EnsureDbMonitorEventsHooked(_cachedDbMonitor);
            return _cachedDbMonitor;
        }

        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                _cachedDbMonitor = app.Services.GetService(typeof(IDbConnectionMonitorService)) as IDbConnectionMonitorService;
                if (_cachedDbMonitor is not null)
                {
                    EnsureDbMonitorEventsHooked(_cachedDbMonitor);
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

    private void EnsureDbMonitorEventsHooked(IDbConnectionMonitorService monitor)
    {
        if (_dbMonitorEventsHooked)
        {
            return;
        }

        monitor.Disconnected += OnDbMonitorDisconnected;
        monitor.Reconnected += OnDbMonitorReconnected;
        monitor.Reconnected += OnDbMonitorReconnectedForToastSuppress;
        _dbMonitorEventsHooked = true;

        // If page initializes while DB is already disconnected, trigger the same
        // auto-refresh path as a disconnect signal so busy state appears immediately.
        if (!monitor.IsConnected && AutoRefreshOnDbDisconnected)
        {
            ScheduleAutoRefreshFromDbSignal();
        }
    }

    private void OnDbMonitorDisconnected()
    {
        if (!AutoRefreshOnDbDisconnected)
        {
            return;
        }

        ScheduleAutoRefreshFromDbSignal();
    }

    private void OnDbMonitorReconnectedForToastSuppress()
        => _reconnectToastSuppressUntil = DateTimeOffset.UtcNow + ReconnectToastSuppressWindow;

    private void OnDbMonitorReconnected()
    {
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

        PostOnUi(() => _ = ExecuteAutoRefreshFromDbSignalAsync(), DispatcherPriority.Background);
    }

    private async Task ExecuteAutoRefreshFromDbSignalAsync()
    {
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
                _cachedDbMonitor.Reconnected -= OnDbMonitorReconnectedForToastSuppress;
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
    }
}
