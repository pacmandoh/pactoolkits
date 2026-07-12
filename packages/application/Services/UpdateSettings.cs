using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

public sealed class UpdateSettingsService : IUpdateSettingsService
{
    private static readonly string[] SupportedChannels = ["stable", "beta"];
    private readonly IUpdateSettingsStore _store;
    private UpdateOptions _current = new();

    public UpdateOptions Current => Clone(_current);
    public event Action? Changed;

    public UpdateSettingsService(IUpdateSettingsStore store)
    {
        _store = store;
        Reload();
    }

    public async Task SaveAsync(UpdateOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        await _store.SaveAsync(Clone(normalized), ct).ConfigureAwait(false);

        _current = normalized;
        Changed?.Invoke();
    }

    public async Task SaveIgnoredVersionAsync(string version, CancellationToken ct = default)
    {
        var next = Current;
        next.IgnoredVersion = (version ?? string.Empty).Trim();
        await SaveAsync(next, ct).ConfigureAwait(false);
    }

    public void Reload()
    {
        _current = Normalize(_store.Load());
        Changed?.Invoke();
    }

    private static UpdateOptions Normalize(UpdateOptions? source)
    {
        var defaults = new UpdateOptions();
        var options = source ?? new UpdateOptions();

        return new UpdateOptions
        {
            AutoCheckOnStartup = options.AutoCheckOnStartup,
            Channel = NormalizeChannel(options.Channel, defaults.Channel),
            ValidatedChannel = NormalizeValidatedChannel(options.ValidatedChannel),
            FeedUrl = string.IsNullOrWhiteSpace(options.FeedUrl) ? defaults.FeedUrl : options.FeedUrl.Trim(),
            AutoCheckIntervalMinutes = options.AutoCheckIntervalMinutes < 0
                ? defaults.AutoCheckIntervalMinutes
                : Math.Clamp(options.AutoCheckIntervalMinutes, 0, 720),
            IgnoredVersion = (options.IgnoredVersion ?? string.Empty).Trim()
        };
    }

    private static UpdateOptions Clone(UpdateOptions source) => new()
    {
        AutoCheckOnStartup = source.AutoCheckOnStartup,
        Channel = source.Channel,
        ValidatedChannel = source.ValidatedChannel,
        FeedUrl = source.FeedUrl,
        AutoCheckIntervalMinutes = source.AutoCheckIntervalMinutes,
        IgnoredVersion = source.IgnoredVersion
    };

    private static string NormalizeChannel(string? channel, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? fallback : channel.Trim().ToLowerInvariant();
        return Array.Exists(SupportedChannels, x => string.Equals(x, normalized, StringComparison.Ordinal))
            ? normalized
            : fallback;
    }

    private static string NormalizeValidatedChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return string.Empty;
        }

        var normalized = channel.Trim().ToLowerInvariant();
        return Array.Exists(SupportedChannels, x => string.Equals(x, normalized, StringComparison.Ordinal))
            ? normalized
            : string.Empty;
    }
}
