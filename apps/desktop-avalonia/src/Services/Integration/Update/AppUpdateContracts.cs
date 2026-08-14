using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Update;

/// <summary>定义应用更新检查、下载准备和重启安装契约</summary>
public interface IAppUpdateService
{
    string CurrentVersion { get; }
    string LatestVersion { get; }
    bool HasUpdateAvailable { get; }
    bool? UpdateAvailability { get; }
    bool IsChecking { get; }
    DateTimeOffset? LastCheckedAt { get; }
    string LastMessage { get; }
    UpdateTargetState? Target { get; }
    event Action? Changed;

    Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default);
    Task<AppUpdateApplyResult> ApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> RestartToApplyAsync(CancellationToken ct = default);

    /// <summary>
    /// 将配置通道与 Velopack 安装通道对齐；空 stamp 且不一致时经 <paramref name="confirmMismatchAsync"/> 确认
    /// </summary>
    Task AlignChannelAsync(
        Func<string, string, CancellationToken, Task<bool>>? confirmMismatchAsync = null,
        CancellationToken ct = default);
}

/// <summary>应用更新检查结果</summary>
public sealed record AppUpdateCheckResult(
    bool Success,
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string Message,
    DateTimeOffset CheckedAt);

/// <summary>应用更新下载与准备结果</summary>
public sealed record AppUpdateApplyResult(
    bool Success,
    string Message);

/// <summary>下一目标版本从发现候选到安装恢复的统一状态</summary>
public sealed record UpdateTargetState(
    string Version,
    string Channel,
    string FeedUrl,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? DownloadedAt,
    UpdateTargetStage Stage,
    string Message);

/// <summary>用户可感知的目标版本阶段，不包含瞬时校验步骤</summary>
public enum UpdateTargetStage
{
    Available,
    Downloading,
    ReadyToInstall,
    Blocked
}
