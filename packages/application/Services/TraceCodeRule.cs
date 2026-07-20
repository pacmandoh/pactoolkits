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

    public void Apply(TraceCodeValidationOptions options)
    {
        var normalized = Normalize(options);
        lock (_gate)
        {
            if (Equals(_current, normalized))
            {
                return;
            }

            _current = Clone(normalized);
        }

        Changed?.Invoke();
    }

    public void Reload() => Apply(_store.Load());

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

    private static bool Equals(TraceCodeValidationOptions left, TraceCodeValidationOptions right)
        => left.RequiredLength == right.RequiredLength
           && string.Equals(left.Pattern, right.Pattern, StringComparison.Ordinal);

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
