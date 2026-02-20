using pactoolkits_ui.Behaviors;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using pactoolkits_ui.Common;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.DataAccess;
using Microsoft.Extensions.Options;
using pactoolkits_ui.Services;

namespace pactoolkits_ui.ViewModels;

public abstract class AppPageBase : ViewModelBase, ITopBarActions, IDisposable
{
    public abstract string DisplayName { get; }
    public abstract MaterialIconKind Icon { get; }
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

    private readonly PageReloadBehavior _reload = new();

    private PgOptions? _cachedPgOptions;
    private IDbConnectionMonitorService? _cachedDbMonitor;

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        protected set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _refreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    protected bool IsDbConnected => _cachedDbMonitor?.IsConnected ?? true;

    protected AppPageBase()
    {
        _refreshCommand = new AsyncRelayCommand(
            execute: ExecuteRefreshAsync,
            canExecute: CanRefresh);
    }

    protected virtual Task ReloadCoreAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual void OnReloadFinished() { }

    protected Task RefreshPageAsync() => _refreshCommand.ExecuteAsync(null);

    protected static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    protected static void NotifyCommands(params IRelayCommand?[] commands)
    {
        foreach (var command in commands)
            command?.NotifyCanExecuteChanged();
    }

    protected static Task RunOnUiAsync(Action action)
        => UiThreadHelper.RunOnUiAsync(action, DispatcherPriority.Background);

    protected static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
        => await UiThreadHelper.RunOnUiAsync(action, priority);

    protected static void PostOnUi(Action action)
        => UiThreadHelper.PostOnUi(action, DispatcherPriority.Background);

    protected static void PostOnUi(Action action, DispatcherPriority priority)
        => UiThreadHelper.PostOnUi(action, priority);

    private bool CanRefresh() => IsEnabled;

    protected void LogWarn(string eventName, string message, Exception? ex = null, object? context = null)
        => AppLog.Warn(GetType().Name, eventName, message, ex, context);

    protected void LogError(string eventName, string message, Exception? ex = null, object? context = null)
        => AppLog.Error(GetType().Name, eventName, message, ex, context);

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
            action: async ct =>
            {
                // Reload policy:
                // 1) Apply one unified timeout for all pages.
                // 2) If DB is disconnected, wait for reconnect within that timeout.
                using var timeoutCts = CreateReloadTimeoutCts(ct);
                var tct = timeoutCts.Token;

                var mon = GetDbMonitor();
                if (mon is not null && !mon.IsConnected)
                {
                    var ok = await WaitForConnectedAsync(mon, tct).ConfigureAwait(false);
                    if (!ok)
                        return;
                }

                await action(tct).ConfigureAwait(false);
            },
            onFinished: onFinished);
    }

    protected Task RunLocalReloadAsync(
        Action<bool> setBusy,
        Func<CancellationToken, Task> action,
        Action? onFinished = null)
    {
        return _reload.RunAsync(
            setBusy: setBusy,
            action: async ct =>
            {
                using var timeoutCts = CreateReloadTimeoutCts(ct);
                var tct = timeoutCts.Token;

                var mon = GetDbMonitor();
                if (mon is not null && !mon.IsConnected)
                {
                    var ok = await WaitForConnectedAsync(mon, tct).ConfigureAwait(false);
                    if (!ok)
                        return;
                }

                try
                {
                    await action(tct).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsDbTransportError(ex))
                {
                    LogWarn("reload.local.db_transport_error", "Local reload hit DB transport error", ex);
                    // Keep local reload behavior aligned with page reload:
                    // transport failures mark DB disconnected and wait for reconnect/timeout.
                    if (mon is null)
                        mon = GetDbMonitor();

                    mon?.Signal();
                    if (mon is not null)
                        await WaitForConnectedAsync(mon, tct).ConfigureAwait(false);
                }
            },
            onFinished: onFinished);
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
                    return;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => setBusy(true),
                    Avalonia.Threading.DispatcherPriority.Background);
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

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => setBusy(false),
                Avalonia.Threading.DispatcherPriority.Background);
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
    {
        if (ex is Npgsql.NpgsqlException) return true;
        if (ex is System.IO.EndOfStreamException) return true;
        if (ex is System.IO.IOException) return true;

        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is Npgsql.NpgsqlException) return true;
            if (inner is System.IO.EndOfStreamException) return true;
            if (inner is System.IO.IOException) return true;
            inner = inner.InnerException;
        }

        return false;
    }

    protected async Task<bool> WaitForReconnectOrTimeoutAsync(CancellationToken ct)
    {
        var mon = GetDbMonitor();
        if (mon is null)
            return false;

        return await WaitForConnectedAsync(mon, ct).ConfigureAwait(false);
    }

    protected void SignalDbDisconnected()
    {
        GetDbMonitor()?.Signal();
    }

    private CancellationTokenSource CreateReloadTimeoutCts(CancellationToken ct)
    {
        // Use connect timeout as baseline so UI wait time follows DB configuration.
        var opt = GetPgOptions();
        var connect = opt?.ConnectTimeoutSeconds ?? 5;
        if (connect <= 0) connect = 5;

        var timeoutSeconds = Math.Clamp(Math.Max(10, connect + 2), 5, 30);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }

    private PgOptions? GetPgOptions()
    {
        if (_cachedPgOptions is not null)
            return _cachedPgOptions;

        try
        {
            if (Application.Current is App app)
            {
                var opt = app.Services.GetService(typeof(IOptions<PgOptions>)) as IOptions<PgOptions>;
                _cachedPgOptions = opt?.Value;
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_pg_options.fail", "Failed to resolve PgOptions from DI", ex);
        }

        return _cachedPgOptions;
    }

    private IDbConnectionMonitorService? GetDbMonitor()
    {
        if (_cachedDbMonitor is not null)
            return _cachedDbMonitor;

        try
        {
            if (Application.Current is App app)
            {
                _cachedDbMonitor = app.Services.GetService(typeof(IDbConnectionMonitorService)) as IDbConnectionMonitorService;
            }
        }
        catch (System.Exception ex)
        {
            LogWarn("reload.get_db_monitor.fail", "Failed to resolve DB monitor from DI", ex);
        }

        return _cachedDbMonitor;
    }

    private static async Task<bool> WaitForConnectedAsync(IDbConnectionMonitorService mon, CancellationToken ct)
    {
        if (mon.IsConnected)
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReconnected() => tcs.TrySetResult(true);

        mon.Reconnected += OnReconnected;

        try
        {
            if (mon.IsConnected)
                return true;

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

    public virtual void Dispose()
    {
        _reload.Dispose();
        _cachedDbMonitor = null;
    }
}
