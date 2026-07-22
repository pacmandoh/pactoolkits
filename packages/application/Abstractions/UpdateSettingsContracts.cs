namespace PacToolkits.Application.Abstractions;

/// <summary>应用更新偏好（通道、Feed URL、忽略版本等）</summary>
public sealed class UpdateOptions
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = "stable";
    public string FeedUrl { get; set; } = "https://updates.pacdocs.com/feed/pactoolkits";
    public int AutoCheckIntervalMinutes { get; set; }
    public string IgnoredVersion { get; set; } = string.Empty;
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
