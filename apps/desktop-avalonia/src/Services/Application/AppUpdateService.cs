using System;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

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

public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    private readonly IUpdateSettingsService _settings;
    private readonly IAppLogger _logger;
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
    {
        _settings = settings;
        _logger = logger;
        CurrentVersion = ResolveInstalledVersion();
        LatestVersion = CurrentVersion;

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
            var targetChannel = NormalizeChannel(options.Channel);
            var currentChannel = ResolveInstalledChannel();
            var source = BuildSource(options);

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

            if (!string.IsNullOrWhiteSpace(currentChannel)
                && !string.Equals(currentChannel, targetChannel, StringComparison.OrdinalIgnoreCase))
            {
                var mismatch = CreateCheckResult(
                    success: true,
                    hasUpdate: false,
                    hasProductUpdate: false,
                    latestVersion: CurrentVersion,
                    currentChannel: currentChannel,
                    targetChannel: targetChannel,
                    channelSwitchRequired: true,
                    message: $"当前程序通道：{currentChannel}；目标通道：{targetChannel}；建议动作：下载安装 {targetChannel} 通道最新安装包完成切换",
                    checkedAt: now,
                    source: source);
                _logger.Warn("AppUpdateService", "update.check.channel_mismatch", "Installed channel differs from selected channel", null, new
                {
                    CurrentVersion,
                    CurrentChannel = currentChannel,
                    TargetChannel = targetChannel
                });
                SetState(mismatch);
                return mismatch;
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
            var ignored = string.Equals(latest, options.IgnoredVersion, StringComparison.Ordinal);
            var hasUpdate = !ignored;
            var message = hasUpdate
                ? $"发现新版本 {latest}"
                : $"已忽略版本 {latest}";
            var result = CreateCheckResult(
                success: true,
                hasUpdate: hasUpdate,
                hasProductUpdate: ignored ? false : true,
                latestVersion: latest,
                currentChannel: currentChannel,
                targetChannel: targetChannel,
                channelSwitchRequired: false,
                message: message,
                checkedAt: now,
                source: source);
            _logger.Info("AppUpdateService", "update.check.available", "Update check found release", new
            {
                CurrentVersion,
                LatestVersion = latest,
                CurrentChannel = currentChannel,
                TargetChannel = targetChannel,
                Ignored = ignored
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
                targetChannel: NormalizeChannel(_settings.Current.Channel),
                channelSwitchRequired: false,
                message: $"更新源连接失败：{ex.Message}",
                checkedAt: DateTimeOffset.Now,
                source: BuildSource(_settings.Current));
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
            var options = _settings.Current;
            var currentChannel = ResolveInstalledChannel();
            var targetChannel = NormalizeChannel(options.Channel);
            if (string.IsNullOrWhiteSpace(options.FeedUrl))
                return new AppUpdateApplyResult(false, false, "未配置更新源地址", CurrentVersion);

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
                return new AppUpdateApplyResult(false, false, "当前不是 Velopack 安装包运行，无法在线更新", CurrentVersion);

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

            if (!string.IsNullOrWhiteSpace(currentChannel)
                && !string.Equals(currentChannel, targetChannel, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn("AppUpdateService", "update.apply.channel_mismatch", "Blocked update apply because installed channel differs from selected channel", null, new
                {
                    CurrentVersion,
                    CurrentChannel = currentChannel,
                    TargetChannel = targetChannel
                });
                return new AppUpdateApplyResult(false, false, $"当前程序通道：{currentChannel}；目标通道：{targetChannel}；建议动作：下载安装 {targetChannel} 通道最新安装包完成切换", CurrentVersion);
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
            if (string.Equals(latest, options.IgnoredVersion, StringComparison.Ordinal))
                return new AppUpdateApplyResult(false, false, $"已忽略版本 {latest}", latest);

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
                return false;

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
                return false;

            var pending = mgr.UpdatePendingRestart;
            if (pending is null)
                return false;

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
        LatestVersion = CurrentVersion;
        HasUpdateAvailable = false;
        HasProductUpdateAvailable = null;
        Changed?.Invoke();
        _ = RecheckAfterSettingsChangedAsync();
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
            return;

        IsChecking = value;
        Changed?.Invoke();
    }

    private static UpdateManager CreateUpdateManager(PacToolkits.Desktop.Avalonia.Services.Infrastructure.UpdateOptions options)
    {
        var explicitChannel = string.IsNullOrWhiteSpace(options.Channel) ? "stable" : options.Channel.Trim().ToLowerInvariant();
        var feed = ResolveChannelFeedUrl(options.FeedUrl, explicitChannel);
        var updateOptions = new Velopack.UpdateOptions
        {
            ExplicitChannel = explicitChannel
        };

        return new UpdateManager(feed, updateOptions);
    }

    private static string BuildSource(PacToolkits.Desktop.Avalonia.Services.Infrastructure.UpdateOptions options)
    {
        var channel = string.IsNullOrWhiteSpace(options.Channel) ? "stable" : options.Channel.Trim().ToLowerInvariant();
        var feed = ResolveChannelFeedUrl(options.FeedUrl, channel);
        return $"{channel} @ {feed}";
    }

    private static string ResolveChannelFeedUrl(string? baseFeedUrl, string channel)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseFeedUrl) ? string.Empty : baseFeedUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
            return string.Empty;

        if (normalizedBase.EndsWith("/stable", StringComparison.OrdinalIgnoreCase)
            || normalizedBase.EndsWith("/beta", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = normalizedBase.LastIndexOf('/');
            if (lastSlash > 0)
                normalizedBase = normalizedBase[..lastSlash];
        }

        return $"{normalizedBase}/{channel}";
    }

    private void RefreshCurrentVersion()
    {
        var installed = ResolveInstalledVersion();
        if (string.Equals(CurrentVersion, installed, StringComparison.Ordinal))
            return;

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
                    return version;
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
                    return channel;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string NormalizeChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant();
        return normalized is "stable" or "beta" ? normalized : "stable";
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
