using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using Velopack;
using Velopack.Locators;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Update;

/// <summary>
/// 应用更新服务
///
/// 负责把 manifest、Velopack 候选与数据库状态绑定后下载并应用安装包；不含 UI 流程编排
/// </summary>
public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    private sealed record PendingEvaluation(UpdateTargetState State, bool VersionChanged);

    private readonly IUpdateSettingsService _settings;
    private readonly IAppLogger _logger;
    private readonly IReleaseManifestProbeService _manifestProbe;
    private readonly ISettingsService _appSettings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _checkedVersion;
    private UpdateTargetState? _checkedTarget;
    private PacToolkits.Application.Abstractions.UpdateOptions _observedOptions;

    public string CurrentVersion { get; private set; }
    public string LatestVersion { get; private set; }
    public bool HasUpdateAvailable { get; private set; }
    public bool? UpdateAvailability { get; private set; }
    public bool IsChecking { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public string LastMessage { get; private set; } = "未检查";
    public UpdateTargetState? Target { get; private set; }

    public event Action? Changed;

    public AppUpdateService(
        IUpdateSettingsService settings,
        IAppLogger logger,
        IReleaseManifestProbeService manifestProbe,
        ISettingsService appSettings)
    {
        _settings = settings;
        _logger = logger;
        _manifestProbe = manifestProbe;
        _appSettings = appSettings;
        _observedOptions = settings.Current;
        CurrentVersion = ResolveInstalledVersion();
        LatestVersion = CurrentVersion;
        LoadPendingState();

        _settings.Changed += OnSettingsChanged;
    }

    private void LoadPendingState()
    {
        try
        {
            var options = _settings.Current;
            var manager = CreateUpdateManager(options);
            var pending = manager.IsInstalled ? manager.UpdatePendingRestart : null;
            if (pending is null)
            {
                return;
            }

            var pendingVersion = pending.Version.ToString();
            Target = new UpdateTargetState(
                pendingVersion,
                AppUpdatePolicy.NormalizeChannel(options.Channel),
                AppUpdatePolicy.ResolveFeedUrl(
                    options.FeedUrl,
                    AppUpdatePolicy.NormalizeChannel(options.Channel)),
                BuildDatabaseLabel(_appSettings.AppliedDb),
                "unknown",
                "unknown",
                null,
                null,
                UpdateTargetStage.ReadyToInstall,
                "已下载更新将在安装前重新验证");
            LatestVersion = pendingVersion;
            HasUpdateAvailable = true;
            UpdateAvailability = true;
            LastMessage = Target.Message;
        }
        catch (Exception ex)
        {
            _logger.Warn("AppUpdateService", "update.pending.load_fail",
                "Failed to load pending update state", ex);
        }
    }

    private AppUpdateCheckResult CreateCheckResult(
        bool success,
        bool hasUpdate,
        string latestVersion,
        string message,
        DateTimeOffset checkedAt)
        => new(
            success,
            hasUpdate,
            CurrentVersion,
            latestVersion,
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
            _checkedVersion = null;
            _checkedTarget = null;

            _logger.Info("AppUpdateService", "update.check.start", "Starting update check", new
            {
                CurrentVersion,
                CurrentChannel = currentChannel,
                TargetChannel = targetChannel,
                Feed = source
            });

            if (string.IsNullOrWhiteSpace(options.FeedUrl))
            {
                Target = null;
                var noFeed = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    latestVersion: CurrentVersion,
                    message: "未配置更新源地址",
                    checkedAt: now);
                SetState(noFeed);
                return noFeed;
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                Target = null;
                var notInstalled = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    latestVersion: CurrentVersion,
                    message: "当前不是 Velopack 安装包运行，无法在线更新",
                    checkedAt: now);
                SetState(notInstalled);
                return notInstalled;
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is not null)
            {
                var pendingVersion = pending.Version.ToString();
                var pendingEvaluation = await EvaluatePendingAsync(
                    pendingVersion,
                    options,
                    ct).ConfigureAwait(false);
                var pendingState = pendingEvaluation.State;
                Target = pendingState;
                if (pendingState.Stage == UpdateTargetStage.Blocked)
                {
                    var invalidPending = CreateCheckResult(
                        success: false,
                        hasUpdate: true,
                        latestVersion: pendingVersion,
                        message: pendingState.Message,
                        checkedAt: now);
                    SetState(invalidPending);
                    return invalidPending;
                }

                var pendingResult = CreateCheckResult(
                    success: true,
                    hasUpdate: true,
                    latestVersion: pendingVersion,
                    message: $"更新已下载：{pendingVersion}，等待重启应用",
                    checkedAt: now);
                SetState(pendingResult);
                return pendingResult;
            }

            Target = null;
            var pgOptions = _appSettings.AppliedDb;
            var probe = await _manifestProbe.ProbeAsync(
                options.FeedUrl,
                targetChannel,
                pgOptions,
                ct).ConfigureAwait(false);
            if (!probe.Success)
            {
                var blocked = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    latestVersion: CurrentVersion,
                    message: probe.Message,
                    checkedAt: now);
                _logger.Warn("AppUpdateService", "update.check.compatibility_blocked",
                    "Update check blocked by release or database compatibility", null, new
                    {
                        CurrentChannel = currentChannel,
                        TargetChannel = targetChannel,
                        probe.FeedManifestUrl,
                        probe.CurrentDbSchema,
                        probe.RequiredMinDbSchema,
                        probe.RequiredMaxDbSchema
                    });
                SetState(blocked);
                return blocked;
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
            {
                var upToDate = CreateCheckResult(
                    success: true,
                    hasUpdate: false,
                    latestVersion: CurrentVersion,
                    message: "当前已是最新版本",
                    checkedAt: now);
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
            if (!string.Equals(latest, probe.ManifestProductVersion, StringComparison.Ordinal))
            {
                var mismatch = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    latestVersion: latest,
                    message: $"更新源发布状态不一致：Feed 为 {latest}，清单为 {probe.ManifestProductVersion}",
                    checkedAt: now);
                SetState(mismatch);
                return mismatch;
            }

            var decision = AppUpdatePolicy.EvaluateRelease(latest, options.IgnoredVersion);
            _checkedTarget = CreateTarget(
                latest,
                targetChannel,
                probe,
                pgOptions,
                now,
                downloadedAt: null,
                UpdateTargetStage.Available,
                "已通过兼容性检查，点击立即更新后下载并安装");
            Target = decision.HasUpdate ? _checkedTarget : null;
            var result = CreateCheckResult(
                success: true,
                hasUpdate: decision.HasUpdate,
                latestVersion: latest,
                message: decision.Message,
                checkedAt: now);
            _checkedVersion = latest;
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
                latestVersion: CurrentVersion,
                message: $"更新源连接失败：{ex.Message}",
                checkedAt: DateTimeOffset.Now);
            _logger.Error("AppUpdateService", "update.check.fail", "Update check failed", ex, new
            {
                CurrentVersion,
                Source = AppUpdatePolicy.BuildSource(_settings.Current)
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
            var targetChannel = AppUpdatePolicy.NormalizeChannel(options.Channel);
            if (string.IsNullOrWhiteSpace(options.FeedUrl))
            {
                return new AppUpdateApplyResult(false, "未配置更新源地址");
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                return new AppUpdateApplyResult(false, "当前不是 Velopack 安装包运行，无法在线更新");
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is not null)
            {
                _checkedVersion = null;
                var pendingVersion = pending.Version.ToString();
                var pendingEvaluation = await EvaluatePendingAsync(
                    pendingVersion,
                    options,
                    ct).ConfigureAwait(false);
                var pendingState = pendingEvaluation.State;
                Target = pendingState;
                if (pendingState.Stage == UpdateTargetStage.Blocked)
                {
                    if (!pendingEvaluation.VersionChanged)
                    {
                        Changed?.Invoke();
                        return new AppUpdateApplyResult(false, pendingState.Message);
                    }

                    Target = null;
                    Changed?.Invoke();
                    _logger.Info("AppUpdateService", "update.apply.pending_superseded",
                        "Superseded pending package will be replaced by the current release", new
                        {
                            PendingVersion = pendingVersion,
                            Reason = pendingState.Message,
                            TargetChannel = targetChannel
                        });
                }
                else
                {
                    _logger.Info("AppUpdateService", "update.apply.pending", "Update already pending restart", new
                    {
                        CurrentVersion,
                        PendingVersion = pendingVersion,
                        CurrentChannel = currentChannel
                    });
                    return new AppUpdateApplyResult(true, $"更新已下载：{pendingVersion}，可直接重启应用");
                }
            }

            var pgOptions = _appSettings.AppliedDb;
            var probe = await _manifestProbe.ProbeAsync(
                options.FeedUrl,
                targetChannel,
                pgOptions,
                ct).ConfigureAwait(false);
            if (!probe.Success)
            {
                _checkedVersion = null;
                ClearCandidate(probe.Message);
                return new AppUpdateApplyResult(false, probe.Message);
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
            {
                _checkedVersion = null;
                ClearCandidate("无需更新", upToDate: true);
                _logger.Info("AppUpdateService", "update.apply.none", "Apply requested but no updates were available", new
                {
                    CurrentVersion,
                    CurrentChannel = currentChannel,
                    TargetChannel = targetChannel
                });
                return new AppUpdateApplyResult(false, "无需更新");
            }

            var latest = updates.TargetFullRelease.Version.ToString();
            if (!string.Equals(latest, probe.ManifestProductVersion, StringComparison.Ordinal))
            {
                _checkedVersion = null;
                ClearCandidate("更新源中的版本信息不一致，请稍后重试");
                return new AppUpdateApplyResult(
                    false,
                    "更新源中的版本信息不一致，请稍后重试");
            }

            if (AppUpdatePolicy.IsIgnored(latest, options.IgnoredVersion))
            {
                _checkedVersion = latest;
                _checkedTarget = CreateTarget(
                    latest,
                    targetChannel,
                    probe,
                    pgOptions,
                    DateTimeOffset.Now,
                    downloadedAt: null,
                    UpdateTargetStage.Available,
                    "已通过兼容性检查，点击立即更新后下载并安装");
                Target = null;
                LatestVersion = latest;
                HasUpdateAvailable = false;
                UpdateAvailability = false;
                LastMessage = $"已忽略版本 {latest}";
                Changed?.Invoke();
                return new AppUpdateApplyResult(false, $"已忽略版本 {latest}");
            }

            LatestVersion = latest;
            HasUpdateAvailable = true;
            UpdateAvailability = true;
            _checkedVersion = latest;
            _checkedTarget = CreateTarget(
                latest,
                targetChannel,
                probe,
                pgOptions,
                DateTimeOffset.Now,
                downloadedAt: null,
                UpdateTargetStage.Available,
                "已通过兼容性检查，点击立即更新后下载并安装");
            Target = _checkedTarget with
            {
                Stage = UpdateTargetStage.Downloading,
                Message = "正在下载更新"
            };
            Changed?.Invoke();

            try
            {
                await mgr.DownloadUpdatesAsync(
                    updates,
                    value => progress?.Report(value),
                    ct).ConfigureAwait(false);
            }
            catch
            {
                Target = _checkedTarget with { Message = "下载未完成，可重新尝试" };
                Changed?.Invoke();
                throw;
            }

            Target = _checkedTarget with
            {
                DownloadedAt = DateTimeOffset.Now,
                Stage = UpdateTargetStage.ReadyToInstall,
                Message = "更新已下载，正在准备安装"
            };
            Changed?.Invoke();

            _logger.Info("AppUpdateService", "update.apply.downloaded", "Update package downloaded and pending restart", new
            {
                CurrentVersion,
                LatestVersion = latest,
                CurrentChannel = currentChannel,
                TargetChannel = targetChannel
            });

            return new AppUpdateApplyResult(true, "更新包已准备完成，重启后生效");
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
                SetLastMessage("未配置更新源地址");
                return false;
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                SetLastMessage("当前不是 Velopack 安装包运行，无法在线更新");
                return false;
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is null)
            {
                SetLastMessage("当前没有可安装的更新包");
                return false;
            }

            var pendingVersion = pending.Version.ToString();
            var pendingEvaluation = await EvaluatePendingAsync(
                pendingVersion,
                options,
                ct).ConfigureAwait(false);
            var pendingState = pendingEvaluation.State;
            Target = pendingState;
            if (pendingState.Stage == UpdateTargetStage.Blocked)
            {
                SetLastMessage(pendingState.Message);
                _logger.Warn("AppUpdateService", "update.restart.compatibility_blocked",
                    "Pending update restart blocked by current compatibility check", null, new
                    {
                        PendingVersion = pendingVersion,
                        Reason = pendingState.Message
                    });
                return false;
            }

            _logger.Info("AppUpdateService", "update.restart.apply", "Applying pending update and restarting", new
            {
                CurrentVersion,
                PendingVersion = pendingVersion,
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
        var current = _settings.Current;
        var changes = AppUpdatePolicy.GetChanges(_observedOptions, current);
        _observedOptions = current;
        if (AppUpdatePolicy.RequiresSourceCheck(changes))
        {
            _checkedVersion = null;
            _checkedTarget = null;
            LatestVersion = CurrentVersion;
            HasUpdateAvailable = false;
            UpdateAvailability = null;
            Target = null;
            LastMessage = "更新源已变化，正在重新检查";
            Changed?.Invoke();
            TaskObserve.Observe(CheckAsync(),
                "AppUpdateService", "update.recheck.detached.fail");
            return;
        }

        if ((changes & UpdateSettingsChange.IgnoredVersion) != 0
            && _checkedVersion is not null
            && Target?.Stage is not (UpdateTargetStage.ReadyToInstall or UpdateTargetStage.Blocked))
        {
            var decision = AppUpdatePolicy.EvaluateRelease(
                _checkedVersion,
                current.IgnoredVersion);
            LatestVersion = _checkedVersion;
            HasUpdateAvailable = decision.HasUpdate;
            UpdateAvailability = decision.HasUpdate;
            Target = decision.HasUpdate ? _checkedTarget : null;
            LastMessage = decision.Message;
            Changed?.Invoke();
        }
    }

    private async Task<PendingEvaluation> EvaluatePendingAsync(
        string pendingVersion,
        PacToolkits.Application.Abstractions.UpdateOptions options,
        CancellationToken ct)
    {
        var channel = AppUpdatePolicy.NormalizeChannel(options.Channel);
        var pgOptions = _appSettings.AppliedDb;
        var probe = await _manifestProbe.ProbeAsync(
            options.FeedUrl,
            channel,
            pgOptions,
            ct).ConfigureAwait(false);
        if (!probe.Success)
        {
            return new PendingEvaluation(new UpdateTargetState(
                pendingVersion,
                channel,
                probe.TargetFeedUrl,
                BuildDatabaseLabel(pgOptions),
                probe.RequiredMinDbSchema,
                probe.RequiredMaxDbSchema,
                DateTimeOffset.Now,
                Target?.DownloadedAt,
                UpdateTargetStage.Blocked,
                probe.Message), false);
        }

        if (!string.Equals(pendingVersion, probe.ManifestProductVersion, StringComparison.Ordinal))
        {
            return new PendingEvaluation(new UpdateTargetState(
                pendingVersion,
                channel,
                probe.TargetFeedUrl,
                BuildDatabaseLabel(pgOptions),
                probe.RequiredMinDbSchema,
                probe.RequiredMaxDbSchema,
                DateTimeOffset.Now,
                Target?.DownloadedAt,
                UpdateTargetStage.Blocked,
                $"已下载版本 {pendingVersion} 已不是当前更新源版本 {probe.ManifestProductVersion}"), true);
        }

        return new PendingEvaluation(new UpdateTargetState(
            pendingVersion,
            channel,
            probe.TargetFeedUrl,
            BuildDatabaseLabel(pgOptions),
            probe.RequiredMinDbSchema,
            probe.RequiredMaxDbSchema,
            DateTimeOffset.Now,
            Target?.DownloadedAt,
            UpdateTargetStage.ReadyToInstall,
            "已下载版本与当前通道、Feed 和数据库兼容"), false);
    }

    private static UpdateTargetState CreateTarget(
        string version,
        string channel,
        ReleaseManifestProbe probe,
        PgOptions pgOptions,
        DateTimeOffset checkedAt,
        DateTimeOffset? downloadedAt,
        UpdateTargetStage stage,
        string message)
        => new(
            version,
            channel,
            probe.TargetFeedUrl,
            BuildDatabaseLabel(pgOptions),
            probe.RequiredMinDbSchema,
            probe.RequiredMaxDbSchema,
            checkedAt,
            downloadedAt,
            stage,
            message);

    private void ClearCandidate(string message, bool upToDate = false)
    {
        _checkedVersion = null;
        _checkedTarget = null;
        Target = null;
        LatestVersion = CurrentVersion;
        HasUpdateAvailable = false;
        UpdateAvailability = upToDate ? false : null;
        LastMessage = message;
        Changed?.Invoke();
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
        UpdateAvailability = result.Success ? result.HasUpdate : null;
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

    private void SetLastMessage(string message)
    {
        LastMessage = message;
        Changed?.Invoke();
    }

    private static string BuildDatabaseLabel(PgOptions options)
        => $"{options.Host.Trim()}:{options.Port}/{options.Database.Trim()}";

    private static UpdateManager CreateUpdateManager(PacToolkits.Application.Abstractions.UpdateOptions options)
    {
        var feedChannel = AppUpdatePolicy.NormalizeChannel(options.Channel);
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

}
