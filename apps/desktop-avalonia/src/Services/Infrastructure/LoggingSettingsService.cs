using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public interface ILoggingSettingsService
{
    LoggingOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(LoggingOptions options, CancellationToken ct = default);
    void Apply(LoggingOptions options);
    void Reload();
}

public sealed class LoggingSettingsService : ILoggingSettingsService
{
    private readonly IAppConfigStore _configStore;
    private LoggingOptions _current = new();

    public LoggingOptions Current => Clone(_current);
    public event Action? Changed;

    public LoggingSettingsService(IAppConfigStore configStore)
    {
        _configStore = configStore;
        Reload();
    }

    public async Task SaveAsync(LoggingOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        await _configStore.UpdateAsync(cfg => cfg.Logging = Clone(normalized), ct).ConfigureAwait(false);

        _current = normalized;
        Changed?.Invoke();
    }

    public void Apply(LoggingOptions options)
    {
        var normalized = Normalize(options);
        if (Equals(_current, normalized))
        {
            return;
        }

        _current = normalized;
        Changed?.Invoke();
    }

    public void Reload() => Apply(_configStore.Load().Logging);

    private static bool Equals(LoggingOptions left, LoggingOptions right)
        => left.Enabled == right.Enabled
           && string.Equals(left.MinimumLevel, right.MinimumLevel, StringComparison.Ordinal)
           && left.RetentionDays == right.RetentionDays
           && left.MaxFileSizeMb == right.MaxFileSizeMb
           && string.Equals(left.LogDirectory, right.LogDirectory, StringComparison.Ordinal);

    private static LoggingOptions Normalize(LoggingOptions? source)
    {
        var defaults = new LoggingOptions();
        var options = source ?? new LoggingOptions();

        var level = (options.MinimumLevel ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedLevel = level switch
        {
            "debug" => "Debug",
            "info" => "Info",
            "warn" => "Warn",
            "error" => "Error",
            "fatal" => "Fatal",
            _ => defaults.MinimumLevel
        };

        return new LoggingOptions
        {
            Enabled = options.Enabled,
            MinimumLevel = normalizedLevel,
            RetentionDays = Math.Clamp(options.RetentionDays <= 0 ? defaults.RetentionDays : options.RetentionDays, 1, 180),
            MaxFileSizeMb = Math.Clamp(options.MaxFileSizeMb <= 0 ? defaults.MaxFileSizeMb : options.MaxFileSizeMb, 1, 200),
            LogDirectory = LogDirectory.Resolve(options.LogDirectory).StoredDirectory
        };
    }

    private static LoggingOptions Clone(LoggingOptions source) => new()
    {
        Enabled = source.Enabled,
        MinimumLevel = source.MinimumLevel,
        RetentionDays = source.RetentionDays,
        MaxFileSizeMb = source.MaxFileSizeMb,
        LogDirectory = source.LogDirectory
    };
}
