using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// 更新设置内存态、通道归一与变更通知
/// </summary>
public sealed class UpdateSettingsService : IUpdateSettingsService
{
    private readonly IUpdateSettingsStore _store;
    private UpdateOptions _current = new();

    public UpdateOptions Current => AppUpdatePolicy.NormalizeOptions(_current);
    public event Action? Changed;

    public UpdateSettingsService(IUpdateSettingsStore store)
    {
        _store = store;
        Reload();
    }

    public async Task SaveAsync(UpdateOptions options, CancellationToken ct = default)
    {
        var normalized = AppUpdatePolicy.NormalizeOptions(options);
        await _store.SaveAsync(normalized, ct).ConfigureAwait(false);

        if (Equals(_current, normalized))
        {
            return;
        }

        _current = normalized;
        Changed?.Invoke();
    }

    public void Apply(UpdateOptions options)
    {
        var normalized = AppUpdatePolicy.NormalizeOptions(options);
        if (Equals(_current, normalized))
        {
            return;
        }

        _current = normalized;
        Changed?.Invoke();
    }

    public void Reload() => Apply(_store.Load());

    private static bool Equals(UpdateOptions left, UpdateOptions right)
        => left.AutoCheckOnStartup == right.AutoCheckOnStartup
           && string.Equals(left.Channel, right.Channel, StringComparison.Ordinal)
           && string.Equals(left.FeedUrl, right.FeedUrl, StringComparison.Ordinal)
           && left.AutoCheckIntervalMinutes == right.AutoCheckIntervalMinutes
           && string.Equals(left.IgnoredVersion, right.IgnoredVersion, StringComparison.Ordinal)
           && string.Equals(left.SeenInstalledChannel, right.SeenInstalledChannel, StringComparison.Ordinal);
}
