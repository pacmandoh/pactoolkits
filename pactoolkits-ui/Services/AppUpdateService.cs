using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;

namespace pactoolkits_ui.Services;

public sealed record AppUpdateCheckResult(
    bool Success,
    bool HasUpdate,
    bool? HasSuiteUpdate,
    string CurrentVersion,
    string LatestVersion,
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
    bool? HasSuiteUpdateAvailable { get; }
    DateTimeOffset? LastCheckedAt { get; }
    string LastMessage { get; }
    event Action? Changed;

    Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default);
    Task<AppUpdateApplyResult> ApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> RestartToApplyAsync(CancellationToken ct = default);
}

public sealed class AppUpdateService : IAppUpdateService
{
    private readonly IUpdateSettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string CurrentVersion { get; }
    public string LatestVersion { get; private set; }
    public bool HasUpdateAvailable { get; private set; }
    public bool? HasSuiteUpdateAvailable { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public string LastMessage { get; private set; } = "未检查";

    public event Action? Changed;

    public AppUpdateService(IReleaseVersionService releaseVersion, IUpdateSettingsService settings)
    {
        _settings = settings;
        CurrentVersion = releaseVersion.Current.SuiteVersion;
        LatestVersion = CurrentVersion;

        _settings.Changed += OnSettingsChanged;
    }

    private AppUpdateCheckResult CreateCheckResult(
        bool success,
        bool hasUpdate,
        bool? hasSuiteUpdate,
        string latestVersion,
        string message,
        DateTimeOffset checkedAt,
        string source)
        => new(
            success,
            hasUpdate,
            hasSuiteUpdate,
            CurrentVersion,
            latestVersion,
            source,
            message,
            checkedAt);

    public async Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var options = _settings.Current;
            var now = DateTimeOffset.Now;
            var source = BuildSource(options);

            if (string.IsNullOrWhiteSpace(options.FeedUrl))
            {
                var noFeed = CreateCheckResult(
                    success: false,
                    hasUpdate: false,
                    hasSuiteUpdate: null,
                    latestVersion: CurrentVersion,
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
                    hasSuiteUpdate: null,
                    latestVersion: CurrentVersion,
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
                    hasSuiteUpdate: true,
                    latestVersion: pendingVersion,
                    message: $"更新已下载：{pendingVersion}，等待重启应用",
                    checkedAt: now,
                    source: source);
                SetState(pendingResult);
                return pendingResult;
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
            {
                var upToDate = CreateCheckResult(
                    success: true,
                    hasUpdate: false,
                    hasSuiteUpdate: false,
                    latestVersion: CurrentVersion,
                    message: "当前已是最新版本",
                    checkedAt: now,
                    source: source);
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
                hasSuiteUpdate: ignored ? false : true,
                latestVersion: latest,
                message: message,
                checkedAt: now,
                source: source);
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
                hasSuiteUpdate: null,
                latestVersion: CurrentVersion,
                message: $"更新源连接失败：{ex.Message}",
                checkedAt: DateTimeOffset.Now,
                source: BuildSource(_settings.Current));
            SetState(failed);
            return failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppUpdateApplyResult> ApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var options = _settings.Current;
            if (string.IsNullOrWhiteSpace(options.FeedUrl))
                return new AppUpdateApplyResult(false, false, "未配置更新源地址", CurrentVersion);

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
                return new AppUpdateApplyResult(false, false, "当前不是 Velopack 安装包运行，无法在线更新", CurrentVersion);

            var pending = mgr.UpdatePendingRestart;
            if (pending is not null)
            {
                var pendingVersion = pending.Version.ToString();
                return new AppUpdateApplyResult(true, true, $"更新已下载：{pendingVersion}，可直接重启应用", pendingVersion);
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
                return new AppUpdateApplyResult(false, false, "无需更新", CurrentVersion);

            var latest = updates.TargetFullRelease.Version.ToString();
            if (string.Equals(latest, options.IgnoredVersion, StringComparison.Ordinal))
                return new AppUpdateApplyResult(false, false, $"已忽略版本 {latest}", latest);

            await mgr.DownloadUpdatesAsync(
                updates,
                value => progress?.Report(value),
                ct).ConfigureAwait(false);

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
            var options = _settings.Current;
            if (string.IsNullOrWhiteSpace(options.FeedUrl))
                return false;

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
                return false;

            var pending = mgr.UpdatePendingRestart;
            if (pending is null)
                return false;

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
        LatestVersion = CurrentVersion;
        HasUpdateAvailable = false;
        HasSuiteUpdateAvailable = null;
        LastMessage = "更新配置已变更，请重新检查更新";
        Changed?.Invoke();
    }

    private void SetState(AppUpdateCheckResult result)
    {
        LatestVersion = result.LatestVersion;
        HasUpdateAvailable = result.HasUpdate;
        HasSuiteUpdateAvailable = result.HasSuiteUpdate;
        LastCheckedAt = result.CheckedAt;
        LastMessage = result.Message;
        Changed?.Invoke();
    }

    private static UpdateManager CreateUpdateManager(UpdateOptions options)
    {
        var feed = options.FeedUrl.Trim();
        var explicitChannel = string.IsNullOrWhiteSpace(options.Channel) ? null : options.Channel.Trim();
        var updateOptions = new Velopack.UpdateOptions
        {
            ExplicitChannel = explicitChannel
        };

        return new UpdateManager(feed, updateOptions);
    }

    private static string BuildSource(UpdateOptions options)
    {
        var channel = string.IsNullOrWhiteSpace(options.Channel) ? "stable" : options.Channel.Trim();
        var feed = string.IsNullOrWhiteSpace(options.FeedUrl) ? "unknown" : options.FeedUrl.Trim();
        return $"{channel} @ {feed}";
    }

}
