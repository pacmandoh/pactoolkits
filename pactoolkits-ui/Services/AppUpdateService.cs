using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;

namespace pactoolkits_ui.Services;

public sealed record AppUpdateCheckResult(
    bool Success,
    bool HasUpdate,
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
    DateTimeOffset? LastCheckedAt { get; }
    string LastMessage { get; }
    event Action? Changed;

    Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default);
    Task<AppUpdateApplyResult> ApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> RestartToApplyAsync(CancellationToken ct = default);
}

public sealed class AppUpdateService : IAppUpdateService
{
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IUpdateSettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string CurrentVersion { get; }
    public string LatestVersion { get; private set; }
    public bool HasUpdateAvailable { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public string LastMessage { get; private set; } = "未检查";

    public event Action? Changed;

    public AppUpdateService(IReleaseVersionService releaseVersion, IUpdateSettingsService settings)
    {
        _releaseVersion = releaseVersion;
        _settings = settings;
        CurrentVersion = releaseVersion.Current.UiVersion;
        LatestVersion = CurrentVersion;

        _settings.Changed += OnSettingsChanged;
    }

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
                var noFeed = BuildResult(false, false, CurrentVersion, CurrentVersion, source, "未配置更新源地址", now);
                SetState(noFeed);
                return noFeed;
            }

            var mgr = CreateUpdateManager(options);
            if (!mgr.IsInstalled)
            {
                var notInstalled = BuildResult(false, false, CurrentVersion, CurrentVersion, source, "当前不是 Velopack 安装包运行，无法在线更新", now);
                SetState(notInstalled);
                return notInstalled;
            }

            var pending = mgr.UpdatePendingRestart;
            if (pending is not null)
            {
                var pendingVersion = pending.Version.ToString();
                var pendingResult = BuildResult(true, true, CurrentVersion, pendingVersion, source, $"更新已下载：{pendingVersion}，等待重启应用", now);
                SetState(pendingResult);
                return pendingResult;
            }

            var updates = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates is null)
            {
                var upToDate = BuildResult(true, false, CurrentVersion, CurrentVersion, source, "当前已是最新版本", now);
                SetState(upToDate);
                return upToDate;
            }

            var latest = updates.TargetFullRelease.Version.ToString();
            var ignored = string.Equals(latest, options.IgnoredVersion, StringComparison.Ordinal);
            var hasUpdate = !ignored;
            var message = hasUpdate
                ? $"发现新版本 {latest}"
                : $"已忽略版本 {latest}";
            var result = BuildResult(true, hasUpdate, CurrentVersion, latest, source, message, now);
            SetState(result);
            return result;
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
        var options = _settings.Current;
        LatestVersion = CurrentVersion;
        HasUpdateAvailable = false;
        LastMessage = "更新配置已变更，请重新检查更新";
        Changed?.Invoke();
    }

    private void SetState(AppUpdateCheckResult result)
    {
        LatestVersion = result.LatestVersion;
        HasUpdateAvailable = result.HasUpdate;
        LastCheckedAt = result.CheckedAt;
        LastMessage = result.Message;
        Changed?.Invoke();
    }

    private static AppUpdateCheckResult BuildResult(
        bool success,
        bool hasUpdate,
        string current,
        string latest,
        string source,
        string message,
        DateTimeOffset checkedAt)
        => new(success, hasUpdate, current, latest, source, message, checkedAt);

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
