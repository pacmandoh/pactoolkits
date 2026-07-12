using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

public sealed class TraceCodeRuleService : ITraceCodeRuleService
{
    private readonly ITraceCodeRuleStore _store;
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

    public TraceCodeRuleService(ITraceCodeRuleStore store)
    {
        _store = store;
        Reload();
    }

    public void Reload()
    {
        lock (_gate)
        {
            _current = Normalize(_store.Load());
        }

        Changed?.Invoke();
    }

    public async Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        await _store.SaveAsync(Clone(normalized), ct).ConfigureAwait(false);

        lock (_gate)
        {
            _current = Clone(normalized);
        }

        Changed?.Invoke();
    }

    private static TraceCodeValidationOptions Normalize(TraceCodeValidationOptions? src)
    {
        var o = src ?? new TraceCodeValidationOptions();
        if (o.RequiredLength <= 0)
        {
            o.RequiredLength = 20;
        }

        if (string.IsNullOrWhiteSpace(o.Pattern))
        {
            o.Pattern = "^8\\d+$";
        }

        return o;
    }

    private static TraceCodeValidationOptions Clone(TraceCodeValidationOptions src) => new()
    {
        RequiredLength = src.RequiredLength,
        Pattern = src.Pattern
    };
}
