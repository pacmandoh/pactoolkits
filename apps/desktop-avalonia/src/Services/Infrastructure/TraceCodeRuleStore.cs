using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>追溯码规则本地存储</summary>
public sealed class TraceCodeRuleStore(IAppConfigStore configStore) : ITraceCodeRuleStore
{
    public TraceCodeValidationOptions Load()
        => configStore.Load().TraceCodeValidation;

    public Task SaveAsync(TraceCodeValidationOptions options, CancellationToken ct = default)
        => configStore.UpdateAsync(cfg => cfg.TraceCodeValidation = options, ct);
}
