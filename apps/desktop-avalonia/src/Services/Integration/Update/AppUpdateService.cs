using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using Velopack;
using Velopack.Locators;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Update;

public sealed record AppUpdateCheckResult(
    bool Success,
    bool HasUpdate,
    bool? HasProductUpdate,
    string CurrentVersion,
    string LatestVersion,
    string CurrentChannel,
    string TargetChannel,
    bool ChannelSwitchRequired,
    string Source,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record AppUpdateApplyResult(
    bool Success,
    bool RequiresRestart,
    string Message,
    string TargetVersion);

/// <summary>应用更新检查与应用入口</summary>
public interface IAppUpdateService
{
    string CurrentVersion { get; }
    string LatestVersion { get; }
    bool HasUpdateAvailable { get; }
    bool? HasProductUpdateAvailable { get; }
    bool IsChecking { get; }
    DateTimeOffset? LastCheckedAt { get; }
    string LastMessage { get; }
    event Action? Changed;

    Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default);
    Task<AppUpdateApplyResult> ApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> RestartToApplyAsync(CancellationToken ct = default);
}

/// <summary>
/// 应用更新服务
///
/// 负责检查远端版本并应用安装包；不含 UI 流程编排
/// </summary>
public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    private const string SimulationVersionVariable = "PACTOOLKITS_SIMULATE_UPDATE_VERSION";

    private readonly IUpdateSettingsService _settings;
    private readonly IAppLogger _logger;
    private readonly string? _simulatedVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string CurrentVersion { get; private set; }
    public string LatestVersion { get; private set; }
    public bool HasUpdateAvailable { get; private set; }
    public bool? HasProductUpdateAvailable { get; private set; }
    public bool IsChecking { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public string LastMessage { get; private set; } = "未检查";

    public event Action? Changed;

    public AppUpdateService(IUpdateSettingsService settings, IAppLogger logger)
        : this(settings, logger, Environment.GetEnvironmentVariable(SimulationVersionVariable))
    {
    }

    internal AppUpdateService(
        IUpdateSettingsService settings,
        IAppLogger logger,
        string? simulatedVersion)
    {
        _settings = settings;
        _logger = logger;
        _simulatedVersion = string.IsNullOrWhiteSpace(simulatedVersion)
            ? null
            : simulatedVersion.Trim();
        CurrentVersion = ResolveInstalledVersion();
        LatestVersion = _simulatedVersion ?? CurrentVersion;
        HasUpdateAvailable = _simulatedVersion is not null;
        HasProductUpdateAvailable = _simulatedVersion is null ? null : true;
        if (_simulatedVersion is not null)
        {
            LastMessage = $"模拟发现新版本 {_simulatedVersion}";
        }

        _settings.Changed += OnSettingsChanged;
    }

    private AppUpdateCheckResult CreateCheckResult(
        bool success,
        bool hasUpdate,
        bool? hasProductUpdate,
        string latestVersion,
        string currentChannel,
        string targetChannel,
        bool channelSwitchRequired,
        string message,
        DateTimeOffset checkedAt,
        string source)
        => new(
            success,
            hasUpdate,
            hasProductUpdate,
            CurrentVersion,
            latestVersion,
            currentChannel,
            targetChannel,
            channelSwitchRequired,
            source,
            message,
            checkedAt);

    public async Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        SetChecking(true);
        try
        {
            RefreshCurrentVersion();
            var options = _settings.Current;
            var now = DateTimeOffset.Now;
            var targetChannel = AppUpdatePolicy.NormalizeChannel(options.Channel);
            var currentChannel = ResolveInstalledChannel();
            var source = AppUpdatePolicy.BuildSource(options);

            if (_simulatedVersion is not null)
            {
                var simulated = CreateCheckResult(
                    success: true,
                    hasUpdate: true,
                    hasProductUpdate: true,
                    latestVersion: _simulatedVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: false,
                    message: $"模拟发现新版本 {_simulatedVersion}",
                    checkedAt: now,
                    source: "ui-simulation");
                _logger.Info("AppUpdateService", "update.check.simulated", "Simulated update check found release", new
                {
                    CurrentVersion,
                    LatestVersion = _simulatedVersion,
                    CurrentChannel = currentChannel,
                    TargetChannel = targetChannel
                });
                SetState(simulated);
                return simulated;
            }

            _logger.Info("AppUpdateService", "update.check.start", "Starting update check", new
            {
                CurrentVersion,
                CurrentChannel = currentChannel,
                TargetChannel = targetChannel,
                Feed = source
            });

            if (string.IsNullOrWhiteSpace(options.FeedUrl))
            {
                var noFeed = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    hasProductUpdate: null,
                    latestVersion: CurrentVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: false,
                    message: "未配置更新源地址",
                    checkedAt: now,
                    source: source);
                SetState(noFeed);
                return noFeed;
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                var notInstalled = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    hasProductUpdate: null,
                    latestVersion: CurrentVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: false,
                    message: "当前不是 Velopack 安装包运行，无法在线更新",
                    checkedAt: now,
                    source: source);
                SetState(notInstalled);
                return notInstalled;
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is not null)
            {
                var pendingVersion = pending.Version.ToString();
                var pendingResult = CreateCheckResult(
                    success: true,
                    hasUpdate: true,
                    hasProductUpdate: true,
                    latestVersion: pendingVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: false,
                    message: $"更新已下载：{pendingVersion}，等待重启应用",
                    checkedAt: now,
                    source: source);
                SetState(pendingResult);
                return pendingResult;
            }

            if (!ReleaseChannelAuth.IsAuthorized(
                    targetChannel,
                    currentChannel,
                    options.ValidatedChannel))
            {
                var blockedMessage = ReleaseChannelAuth.BuildBlockedMessage(
                    currentChannel,
                    targetChannel);
                var blocked = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    hasProductUpdate: null,
                    latestVersion: CurrentVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: true,
                    message: blockedMessage,
                    checkedAt: now,
                    source: source);
                _logger.Warn("AppUpdateService", "update.check.channel_unauthorized",
                    "Update check blocked because release channel switch was not validated", null, new
                    {
                        CurrentChannel = currentChannel,
                        TargetChannel = targetChannel,
                        options.ValidatedChannel
                    });
                SetState(blocked);
                return blocked;
            }

            if (!string.IsNullOrWhiteSpace(currentChannel)
                && !string.Equals(currentChannel, targetChannel, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("AppUpdateService", "update.check.channel_switch",
                    "Checking selected channel after validated channel switch", new
                    {
                        CurrentVersion,
                        CurrentChannel = currentChannel,
                        TargetChannel = targetChannel,
                        options.ValidatedChannel
                    });
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
            {
                var upToDate = CreateCheckResult(
                    success: true,
                    hasUpdate: false,
                    hasProductUpdate: false,
                    latestVersion: CurrentVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: false,
                    message: "当前已是最新版本",
                    checkedAt: now,
                    source: source);
                _logger.Info("AppUpdateService", "update.check.uptodate", "No updates available", new
                {
                    CurrentVersion,
                    CurrentChannel = currentChannel,
                    TargetChannel = targetChannel
                });
                SetState(upToDate);
                return upToDate;
            }

            var latest = updates.TargetFullRelease.Version.ToString();
            var decision = AppUpdatePolicy.EvaluateRelease(latest, options.IgnoredVersion);
            var result = CreateCheckResult(
                success: true,
                hasUpdate: decision.HasUpdate,
                hasProductUpdate: decision.HasProductUpdate,
                latestVersion: latest,
                currentChannel: currentChannel,
                targetChannel: targetChannel,
                channelSwitchRequired: false,
                message: decision.Message,
                checkedAt: now,
                source: source);
            _logger.Info("AppUpdateService", "update.check.available", "Update check found release", new
            {
                CurrentVersion,
                LatestVersion = latest,
                CurrentChannel = currentChannel,
                TargetChannel = targetChannel,
                Ignored = !decision.HasUpdate
            });
            SetState(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failed = CreateCheckResult(
                success: false,
                hasUpdate: false,
                hasProductUpdate: null,
                latestVersion: CurrentVersion,
                currentChannel: ResolveInstalledChannel(),
                targetChannel: AppUpdatePolicy.NormalizeChannel(_settings.Current.Channel),
                channelSwitchRequired: false,
                message: $"更新源连接失败：{ex.Message}",
                checkedAt: DateTimeOffset.Now,
                source: AppUpdatePolicy.BuildSource(_settings.Current));
            _logger.Error("AppUpdateService", "update.check.fail", "Update check failed", ex, new
            {
                CurrentVersion,
                failed.Source
            });
            SetState(failed);
            return failed;
        }
        finally
        {
            SetChecking(false);
            _gate.Release();
        }
    }

    public async Task<AppUpdateApplyResult> ApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RefreshCurrentVersion();
            if (_simulatedVersion is not null)
            {
                foreach (var value in new[] { 0, 20, 40, 60, 80, 100 })
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(value);
                    if (value < 100)
                    {
                        await Task.Delay(140, ct).ConfigureAwait(false);
                    }
                }

                _logger.Info("AppUpdateService", "update.apply.simulated", "Blocked update download in simulation mode", new
                {
                    CurrentVersion,
                    LatestVersion = _simulatedVersion
                });
                return new AppUpdateApplyResult(
                    false,
                    false,
                    "当前为更新模拟模式，不会下载更新包或重启应用",
                    _simulatedVersion);
            }

            var options = _settings.Current;
            var currentChannel = ResolveInstalledChannel();
            var targetChannel = AppUpdatePolicy.NormalizeChannel(options.Channel);
            if (string.IsNullOrWhiteSpace(options.FeedUrl))
            {
                return new AppUpdateApplyResult(false, false, "未配置更新源地址", CurrentVersion);
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                return new AppUpdateApplyResult(false, false, "当前不是 Velopack 安装包运行，无法在线更新", CurrentVersion);
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is not null)
            {
                var pendingVersion = pending.Version.ToString();
                _logger.Info("AppUpdateService", "update.apply.pending", "Update already pending restart", new
                {
                    CurrentVersion,
                    PendingVersion = pendingVersion,
                    CurrentChannel = currentChannel
                });
                return new AppUpdateApplyResult(true, true, $"更新已下载：{pendingVersion}，可直接重启应用", pendingVersion);
            }

            if (!ReleaseChannelAuth.IsAuthorized(
                    targetChannel,
                    currentChannel,
                    options.ValidatedChannel))
            {
                var blockedMessage = ReleaseChannelAuth.BuildBlockedMessage(
                    currentChannel,
                    targetChannel);
                _logger.Warn("AppUpdateService", "update.apply.channel_unauthorized",
                    "Update apply blocked because release channel switch was not validated", null, new
                    {
                        CurrentChannel = currentChannel,
                        TargetChannel = targetChannel,
                        options.ValidatedChannel
                    });
                return new AppUpdateApplyResult(false, false, blockedMessage, CurrentVersion);
            }

            if (!string.IsNullOrWhiteSpace(currentChannel)
                && !string.Equals(currentChannel, targetChannel, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("AppUpdateService", "update.apply.channel_switch",
                    "Applying update from selected channel after validated channel switch", new
                    {
                        CurrentVersion,
                        CurrentChannel = currentChannel,
                        TargetChannel = targetChannel,
                        options.ValidatedChannel
                    });
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
            {
                _logger.Info("AppUpdateService", "update.apply.none", "Apply requested but no updates were available", new
                {
                    CurrentVersion,
                    CurrentChannel = currentChannel,
                    TargetChannel = targetChannel
                });
                return new AppUpdateApplyResult(false, false, "无需更新", CurrentVersion);
            }

            var latest = updates.TargetFullRelease.Version.ToString();
            if (AppUpdatePolicy.IsIgnored(latest, options.IgnoredVersion))
            {
                return new AppUpdateApplyResult(false, false, $"已忽略版本 {latest}", latest);
            }

            await mgr.DownloadUpdatesAsync(
                updates,
                value => progress?.Report(value),
                ct).ConfigureAwait(false);

            _logger.Info("AppUpdateService", "update.apply.downloaded", "Update package downloaded and pending restart", new
            {
                CurrentVersion,
                LatestVersion = latest,
                CurrentChannel = currentChannel,
                TargetChannel = targetChannel
            });

            return new AppUpdateApplyResult(true, true, "更新包已准备完成，重启后生效", latest);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RestartToApplyAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RefreshCurrentVersion();
            var options = _settings.Current;
            if (string.IsNullOrWhiteSpace(options.FeedUrl))
            {
                return false;
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                return false;
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is null)
            {
                return false;
            }

            _logger.Info("AppUpdateService", "update.restart.apply", "Applying pending update and restarting", new
            {
                CurrentVersion,
                PendingVersion = pending.Version.ToString(),
                CurrentChannel = ResolveInstalledChannel()
            });
            mgr.ApplyUpdatesAndRestart(pending, Array.Empty<string>());
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnSettingsChanged()
    {
        RefreshCurrentVersion();
        if (_simulatedVersion is not null)
        {
            LatestVersion = _simulatedVersion;
            HasUpdateAvailable = true;
            HasProductUpdateAvailable = true;
            LastMessage = $"模拟发现新版本 {_simulatedVersion}";
            Changed?.Invoke();
            return;
        }

        LatestVersion = CurrentVersion;
        HasUpdateAvailable = false;
        HasProductUpdateAvailable = null;
        Changed?.Invoke();
        TaskObserve.Observe(RecheckAfterSettingsChangedAsync(), "AppUpdateService", "update.recheck.detached.fail");
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _gate.Dispose();
    }

    private void SetState(AppUpdateCheckResult result)
    {
        LatestVersion = result.LatestVersion;
        HasUpdateAvailable = result.HasUpdate;
        HasProductUpdateAvailable = result.HasProductUpdate;
        LastCheckedAt = result.CheckedAt;
        LastMessage = result.Message;
        Changed?.Invoke();
    }

    private void SetChecking(bool value)
    {
        if (IsChecking == value)
        {
            return;
        }

        IsChecking = value;
        Changed?.Invoke();
    }

    private static UpdateManager CreateUpdateManager(PacToolkits.Application.Abstractions.UpdateOptions options)
    {
        var feedChannel = AppUpdatePolicy.NormalizeFeedChannel(options.Channel);
        var feed = AppUpdatePolicy.ResolveFeedUrl(options.FeedUrl, feedChannel);
        var updateOptions = new Velopack.UpdateOptions
        {
            ExplicitChannel = feedChannel
        };

        return new UpdateManager(feed, updateOptions);
    }

    private void RefreshCurrentVersion()
    {
        var installed = ResolveInstalledVersion();
        if (string.Equals(CurrentVersion, installed, StringComparison.Ordinal))
        {
            return;
        }

        CurrentVersion = installed;
    }

    private static string ResolveInstalledVersion()
    {
        try
        {
            if (VelopackLocator.IsCurrentSet)
            {
                var version = VelopackLocator.Current.CurrentlyInstalledVersion?.ToString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private static string ResolveInstalledChannel()
    {
        try
        {
            if (VelopackLocator.IsCurrentSet)
            {
                var channel = VelopackLocator.Current.Channel?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    return channel;
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private async Task RecheckAfterSettingsChangedAsync()
    {
        try
        {
            await CheckAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

}
