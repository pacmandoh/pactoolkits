namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 追溯码校验规则（长度与正则 Pattern）
/// </summary>
public sealed class TraceCodeValidationOptions
{
    public int RequiredLength { get; set; } = 20;

    public string Pattern { get; set; } = "^8\\d+$";
}

/// <summary>
/// 追溯码校验规则持久化
/// </summary>
public interface ITraceCodeRuleStore
{
    TraceCodeValidationOptions Load();
    Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default);
}

/// <summary>
/// 追溯码校验规则内存态与变更通知
/// </summary>
public interface ITraceCodeRuleService
{
    TraceCodeValidationOptions Current { get; }
    event Action? Changed;
    void Apply(TraceCodeValidationOptions options);
    void Reload();
    Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default);
}
