namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 应用更新偏好（通道、FeedUrl、忽略版本等）
/// </summary>
public sealed class UpdateOptions
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = "stable";
    public string ValidatedChannel { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = "https://updates.pacdocs.com/feed/pactoolkits";
    public int AutoCheckIntervalMinutes { get; set; }
    public string IgnoredVersion { get; set; } = string.Empty;
}

/// <summary>
/// 更新设置持久化
/// </summary>
public interface IUpdateSettingsStore
{
    UpdateOptions Load();
    Task SaveAsync(UpdateOptions options, CancellationToken ct = default);
}

/// <summary>
/// 更新设置内存态、保存与变更通知
/// </summary>
public interface IUpdateSettingsService
{
    UpdateOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(UpdateOptions options, CancellationToken ct = default);
    Task SaveIgnoredVersionAsync(string version, CancellationToken ct = default);
    void Apply(UpdateOptions options);
    void Reload();
}
