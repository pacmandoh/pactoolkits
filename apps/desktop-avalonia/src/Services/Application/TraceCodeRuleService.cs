using System;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public interface ITraceCodeRuleService
{
    TraceCodeValidationOptions Current { get; }
    event Action? Changed;
    void Reload();
    Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default);
}

public sealed class TraceCodeRuleService : ITraceCodeRuleService
{
    private readonly IAppConfigStore _configStore;
    private readonly object _gate = new();
    private TraceCodeValidationOptions _current = new();

    public TraceCodeValidationOptions Current
    {
        get
        {
            lock (_gate)
            {
                return Clone(_current);
            }
        }
    }

    public event Action? Changed;

    public TraceCodeRuleService(IAppConfigStore configStore)
    {
        _configStore = configStore;
        Reload();
    }

    public void Reload()
    {
        lock (_gate)
        {
            var cfg = _configStore.Load();
            _current = Normalize(cfg.TraceCodeValidation);
        }

        Changed?.Invoke();
    }

    public async Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        var cfg = _configStore.Load();
        cfg.TraceCodeValidation = Clone(normalized);
        await _configStore.SaveAsync(cfg, ct).ConfigureAwait(false);

        lock (_gate)
            _current = Clone(normalized);

        Changed?.Invoke();
    }

    private static TraceCodeValidationOptions Normalize(TraceCodeValidationOptions? src)
    {
        var o = src ?? new TraceCodeValidationOptions();
        if (o.RequiredLength <= 0)
            o.RequiredLength = 20;
        if (string.IsNullOrWhiteSpace(o.Pattern))
            o.Pattern = "^8\\d+$";
        return o;
    }

    private static TraceCodeValidationOptions Clone(TraceCodeValidationOptions src) => new()
    {
        RequiredLength = src.RequiredLength,
        Pattern = src.Pattern
    };
}
