using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>应用更新 UI 流程入口</summary>
public interface IUpdateFlowService
{
    bool IsApplying { get; }
    int ApplyProgress { get; }
    event Action? StateChanged;

    Task<AppUpdateCheckResult?> CheckAndHandleAsync(
        bool silent = false,
        Action<AppUpdateCheckResult>? syncState = null,
        string logScope = "UpdateDesktopFlow",
        CancellationToken ct = default);

    Task ApplyUpdateFlowAsync();
}

/// <summary>编排更新检查提示与用户确认流程</summary>
public sealed class UpdateFlowService : IUpdateFlowService
{
    private sealed class ProgressSink(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(10);
    private readonly object _applyingGate = new();

    private readonly IAppUpdateService _updates;
    private readonly IToastService _toasts;
    private readonly Func<string, string, Task<bool>> _confirmRestart;
    private readonly IAppLogger _logger;
    private bool _isApplying;
    private int _applyProgress;

    public bool IsApplying
    {
        get
        {
            lock (_applyingGate)
            {
                return _isApplying;
            }
        }
    }

    public int ApplyProgress
    {
        get
        {
            lock (_applyingGate)
            {
                return _applyProgress;
            }
        }
    }

    public event Action? StateChanged;

    public UpdateFlowService(
        IAppUpdateService updates,
        IToastService toasts,
        IDialogService dialogs,
        IAppLogger logger)
        : this(
            updates,
            toasts,
            (dialogs ?? throw new ArgumentNullException(nameof(dialogs))).Confirm,
            logger)
    {
    }

    internal UpdateFlowService(
        IAppUpdateService updates,
        IToastService toasts,
        Func<string, string, Task<bool>> confirmRestart,
        IAppLogger logger)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
        _confirmRestart = confirmRestart ?? throw new ArgumentNullException(nameof(confirmRestart));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AppUpdateCheckResult?> CheckAndHandleAsync(
        bool silent = false,
        Action<AppUpdateCheckResult>? syncState = null,
        string logScope = "UpdateDesktopFlow",
        CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(UpdateCheckTimeout);
            var result = await _updates.CheckAsync(timeoutCts.Token).ConfigureAwait(false);
            syncState?.Invoke(result);

            _logger.Info(logScope, "update.check.result", "Update check finished", new
            {
                result.Success,
                result.HasUpdate,
                result.CurrentVersion,
                result.LatestVersion,
                result.Message
            });

            if (silent)
            {
                return result;
            }

            if (!result.Success || result.ChannelSwitchRequired)
            {
                _toasts.Warn("应用更新", result.Message);
                return result;
            }

            if (!result.HasUpdate)
            {
                _toasts.Info("应用更新", "当前已是最新版本");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.Warn(logScope, "update.check.cancel", "Update check canceled");
                if (!silent)
                {
                    _toasts.Warn("应用更新", "检查已取消");
                }
            }
            else
            {
                _logger.Warn(logScope, "update.check.timeout",
                    $"Update check timed out after {(int)UpdateCheckTimeout.TotalSeconds}s");
                if (!silent)
                {
                    _toasts.Warn("应用更新", $"检查超时（{(int)UpdateCheckTimeout.TotalSeconds} 秒），请检查更新源连通性后重试");
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(logScope, "update.check.error", "Update check failed", ex);
            if (!silent)
            {
                _toasts.Error("应用更新", ex.Message);
            }

            return null;
        }
    }

    public async Task ApplyUpdateFlowAsync()
    {
        if (!TryBeginApplying())
        {
            return;
        }

        try
        {
            var result = await _updates
                .ApplyAsync(new ProgressSink(SetApplyProgress))
                .ConfigureAwait(false);

            if (!result.Success)
            {
                _toasts.Warn("应用更新", result.Message);
                return;
            }

            var restartNow = await _confirmRestart(
                    "更新包已准备完成",
                    $"目标版本：{result.TargetVersion}\n是否立即重启应用以完成更新？")
                .ConfigureAwait(false);

            if (restartNow)
            {
                var started = await _updates.RestartToApplyAsync().ConfigureAwait(false);
                if (!started)
                {
                    _toasts.Warn("应用更新", "未检测到待应用更新包，请重新检查更新后再试");
                }
            }
            else
            {
                _toasts.Success("应用更新", $"已准备版本 {result.TargetVersion}，可稍后重启生效");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("UpdateDesktopFlow", "update.apply.flow_fail", "Update apply flow failed", ex);
            _toasts.Error("应用更新", ex.Message);
        }
        finally
        {
            EndApplying();
        }
    }

    private bool TryBeginApplying()
    {
        lock (_applyingGate)
        {
            if (_isApplying)
            {
                return false;
            }

            _isApplying = true;
            _applyProgress = 0;
        }

        StateChanged?.Invoke();
        return true;
    }

    private void SetApplyProgress(int value)
    {
        var progress = Math.Clamp(value, 0, 100);
        lock (_applyingGate)
        {
            if (!_isApplying || _applyProgress == progress)
            {
                return;
            }

            _applyProgress = progress;
        }

        StateChanged?.Invoke();
    }

    private void EndApplying()
    {
        lock (_applyingGate)
        {
            _isApplying = false;
            _applyProgress = 0;
        }

        StateChanged?.Invoke();
    }
}
