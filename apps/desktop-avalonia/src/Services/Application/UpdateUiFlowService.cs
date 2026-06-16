using System;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Notifications;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using SukiUI.Enums;
using SukiUI.Toasts;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public interface IUpdateUiFlowService
{
    Task<AppUpdateCheckResult?> CheckAndHandleAsync(
        bool showNoUpdateToast,
        bool startupMode,
        Func<Task> applyNowAction,
        Func<Task>? ignoreVersionAction = null,
        Action<AppUpdateCheckResult>? syncState = null,
        string logScope = "UpdateUiFlow",
        CancellationToken ct = default);

    Task ShowUpdateAvailableToastAsync(
        string currentVersion,
        string latestVersion,
        bool startupMode,
        Func<Task> applyNowAction,
        Func<Task>? ignoreVersionAction = null);
    Task IgnoreVersionAsync(string version);
    Task ApplyUpdateFlowAsync();
}

public sealed class UpdateUiFlowService : IUpdateUiFlowService
{
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(10);
    private readonly object _toastGate = new();

    private readonly IAppUpdateService _updates;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IToastService _toasts;
    private readonly IDialogService _dialogs;
    private readonly ISukiToastManager _toastManager;
    private readonly IAppLogger _logger;
    private ISukiToast? _activeUpdateToast;
    private string _activeUpdateToastKey = string.Empty;

    public UpdateUiFlowService(
        IAppUpdateService updates,
        IUpdateSettingsService updateSettings,
        IToastService toasts,
        IDialogService dialogs,
        ISukiToastManager toastManager,
        IAppLogger logger)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _updateSettings = updateSettings ?? throw new ArgumentNullException(nameof(updateSettings));
        _toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _toastManager = toastManager ?? throw new ArgumentNullException(nameof(toastManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ShowUpdateAvailableToastAsync(
        string currentVersion,
        string latestVersion,
        bool startupMode,
        Func<Task> applyNowAction,
        Func<Task>? ignoreVersionAction = null)
    {
        if (applyNowAction is null)
        {
            throw new ArgumentNullException(nameof(applyNowAction));
        }

        var title = startupMode ? "启动时发现更新" : "发现新版本";
        var content = $"当前 {currentVersion} -> 最新 {latestVersion}";
        var toastKey = $"{currentVersion}->{latestVersion}";
        ignoreVersionAction ??= () => IgnoreVersionAsync(latestVersion);

        await RunOnUiAsync(() =>
        {
            lock (_toastGate)
            {
                if (_activeUpdateToast is not null
                    && string.Equals(_activeUpdateToastKey, toastKey, StringComparison.Ordinal))
                {
                    return;
                }

                if (_activeUpdateToast is not null)
                {
                    _toastManager.Dismiss(_activeUpdateToast);
                }

                _activeUpdateToast = _toastManager.CreateToast()
                .OfType(NotificationType.Information)
                .WithTitle(title)
                .WithContent(content)
                .WithActionButton("稍后", _ => { }, true, SukiButtonStyles.Basic)
                .WithActionButton("忽略此版本", _toast => FireAndForget(ignoreVersionAction, "update.toast.ignore"), true, SukiButtonStyles.Flat)
                .WithActionButton("立即更新", _toast => FireAndForget(applyNowAction, "update.toast.apply"), true, SukiButtonStyles.Accent)
                .Queue();
                _activeUpdateToastKey = toastKey;
            }
        });
    }

    public async Task<AppUpdateCheckResult?> CheckAndHandleAsync(
        bool showNoUpdateToast,
        bool startupMode,
        Func<Task> applyNowAction,
        Func<Task>? ignoreVersionAction = null,
        Action<AppUpdateCheckResult>? syncState = null,
        string logScope = "UpdateUiFlow",
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

            if (!result.Success)
            {
                if (!startupMode)
                {
                    _toasts.Warn("应用更新", result.Message);
                }

                return result;
            }

            if (result.ChannelSwitchRequired)
            {
                _toasts.Warn("应用更新", result.Message);
                return result;
            }

            if (!result.HasUpdate)
            {
                if (showNoUpdateToast)
                {
                    _toasts.Info("应用更新", "当前已是最新版本");
                }

                return result;
            }

            await ShowUpdateAvailableToastAsync(
                result.CurrentVersion,
                result.LatestVersion,
                startupMode,
                applyNowAction,
                ignoreVersionAction ?? (() => IgnoreVersionAsync(result.LatestVersion))).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.Warn(logScope, "update.check.cancel", "Update check canceled");
                _toasts.Warn("应用更新", "检查已取消");
            }
            else
            {
                _logger.Warn(logScope, "update.check.timeout",
                    $"Update check timed out after {(int)UpdateCheckTimeout.TotalSeconds}s");
                _toasts.Warn("应用更新", $"检查超时（{(int)UpdateCheckTimeout.TotalSeconds} 秒），请检查更新源连通性后重试");
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(logScope, "update.check.error", "Update check failed", ex);
            _toasts.Error("应用更新", ex.Message);
            return null;
        }
    }

    public async Task IgnoreVersionAsync(string version)
    {
        await DismissActiveUpdateToastAsync().ConfigureAwait(false);
        await _updateSettings.SaveIgnoredVersionAsync(version).ConfigureAwait(false);
        _toasts.Info("应用更新", $"已忽略版本 {version}");
    }

    public async Task ApplyUpdateFlowAsync()
    {
        ProgressBar? progressBar = null;
        ISukiToast? progressToast = null;

        try
        {
            await DismissActiveUpdateToastAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                progressBar = new ProgressBar
                {
                    MinWidth = 220,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0,
                    ShowProgressText = true
                };

                progressToast = _toastManager.CreateToast()
                    .OfType(NotificationType.Information)
                    .WithTitle("正在下载更新...")
                    .WithContent(progressBar)
                    .Queue();
            });

            var progress = new Progress<int>(value =>
            {
                PostOnUi(() =>
                {
                    progressBar?.Value = Math.Clamp(value, 0, 100);
                });
            });

            var result = await _updates.ApplyAsync(progress).ConfigureAwait(false);

            await RunOnUiAsync(() =>
            {
                if (progressToast is not null)
                {
                    _toastManager.Dismiss(progressToast);
                }

                progressToast = null;
            });

            if (!result.Success)
            {
                _toasts.Warn("应用更新", result.Message);
                return;
            }

            var restartNow = await _dialogs.Confirm(
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
            await RunOnUiAsync(() =>
            {
                if (progressToast is not null)
                {
                    _toastManager.Dismiss(progressToast);
                }
            });

            _logger.Error("UpdateUiFlow", "update.apply.flow_fail", "Update apply flow failed", ex);
            _toasts.Error("应用更新", ex.Message);
        }
    }

    private static Task RunOnUiAsync(Action action)
        => UiThreadHelper.RunOnUiAsync(action);

    private static void PostOnUi(Action action)
        => UiThreadHelper.PostOnUi(action);

    private void FireAndForget(Func<Task> action, string eventName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("UpdateUiFlow", eventName, "Background action from update toast failed", ex);
            }
        });
    }

    private Task DismissActiveUpdateToastAsync()
        => RunOnUiAsync(() =>
        {
            lock (_toastGate)
            {
                if (_activeUpdateToast is not null)
                {
                    _toastManager.Dismiss(_activeUpdateToast);
                }

                _activeUpdateToast = null;
                _activeUpdateToastKey = string.Empty;
            }
        });
}
