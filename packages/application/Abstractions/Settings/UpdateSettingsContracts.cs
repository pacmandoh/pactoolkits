namespace PacToolkits.Application.Abstractions;

/// <summary>应用更新通道、源地址、检查周期和忽略版本等偏好</summary>
public sealed class UpdateOptions
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = "stable";
    public string FeedUrl { get; set; } = "https://updates.pacdocs.com/feed/pactoolkits";
    public int AutoCheckIntervalMinutes { get; set; }
    public string IgnoredVersion { get; set; } = string.Empty;

    public string SeenInstalledChannel { get; set; } = string.Empty;
}

/// <summary>更新设置字段变化对检查候选与轮询调度的影响分类</summary>
[Flags]
public enum UpdateSettingsChange
{
    None = 0,
    Channel = 1,
    FeedUrl = 2,
    IgnoredVersion = 4,
    PollInterval = 8,
    AutoCheck = 16
}

/// <summary>Velopack 安装通道与配置通道的对齐动作</summary>
public enum ChannelAlignAction
{
    None = 0,
    PersistSeenOnly = 1,
    AlignToInstalled = 2,
    ConfirmMismatch = 3
}

/// <summary>安装通道对齐决策</summary>
public readonly record struct ChannelAlignDecision(
    ChannelAlignAction Action,
    string Channel,
    string SeenInstalledChannel,
    string InstalledChannel);
