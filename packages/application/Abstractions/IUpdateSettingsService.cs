namespace PacToolkits.Application.Abstractions;

public sealed class UpdateOptions
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public string Channel { get; set; } = "stable";
    public string ValidatedChannel { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = "https://updates.pacdocs.com/feed/pactoolkits";
    public int AutoCheckIntervalMinutes { get; set; }
    public string IgnoredVersion { get; set; } = string.Empty;
}

public interface IUpdateSettingsStore
{
    UpdateOptions Load();
    Task SaveAsync(UpdateOptions options, CancellationToken ct = default);
}

public interface IUpdateSettingsService
{
    UpdateOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(UpdateOptions options, CancellationToken ct = default);
    Task SaveIgnoredVersionAsync(string version, CancellationToken ct = default);
    void Reload();
}
