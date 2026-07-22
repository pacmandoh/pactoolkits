namespace PacToolkits.Application.Abstractions;

/// <summary>应用更新偏好（通道、Feed URL、忽略版本等）</summary>
public sealed class UpdateOptions
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = "stable";
    public string FeedUrl { get; set; } = "https://updates.pacdocs.com/feed/pactoolkits";
    public int AutoCheckIntervalMinutes { get; set; }
    public string IgnoredVersion { get; set; } = string.Empty;

    /// <summary>
    /// 上次对齐过的 Velopack 安装通道；安装通道相对 stamp 变化时自动收敛，空 stamp 且不一致时询问用户
    /// </summary>
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
