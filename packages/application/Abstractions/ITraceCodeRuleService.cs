namespace PacToolkits.Application.Abstractions;

public sealed class TraceCodeValidationOptions
{
    public int RequiredLength { get; set; } = 20;

    public string Pattern { get; set; } = "^8\\d+$";
}

public interface ITraceCodeRuleStore
{
    TraceCodeValidationOptions Load();
    Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default);
}

public interface ITraceCodeRuleService
{
    TraceCodeValidationOptions Current { get; }
    event Action? Changed;
    void Reload();
    Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default);
}
