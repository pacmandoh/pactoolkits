using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public interface IUiBehaviorService
{
    UiBehaviorOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(UiBehaviorOptions options, CancellationToken ct = default);
    void Apply(UiBehaviorOptions options);
    void Reload();
}

public sealed class UiBehaviorService : IUiBehaviorService
{
    private readonly IAppConfigStore _configStore;
    private UiBehaviorOptions _current = new();

    public UiBehaviorOptions Current => Clone(_current);
    public event Action? Changed;

    public UiBehaviorService(IAppConfigStore configStore)
    {
        _configStore = configStore;
        Reload();
    }

    public async Task SaveAsync(UiBehaviorOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        await _configStore.UpdateAsync(cfg => cfg.UiBehavior = Clone(normalized), ct).ConfigureAwait(false);

        _current = normalized;
        Changed?.Invoke();
    }

    public void Apply(UiBehaviorOptions options)
    {
        var normalized = Normalize(options);
        if (Equals(_current, normalized))
        {
            return;
        }

        _current = normalized;
        Changed?.Invoke();
    }

    public void Reload() => Apply(_configStore.Load().UiBehavior);

    private static bool Equals(UiBehaviorOptions left, UiBehaviorOptions right)
        => left.MinimizeToTrayOnClose == right.MinimizeToTrayOnClose;

    private static UiBehaviorOptions Normalize(UiBehaviorOptions? src)
    {
        var o = src ?? new UiBehaviorOptions();
        return new UiBehaviorOptions
        {
            MinimizeToTrayOnClose = o.MinimizeToTrayOnClose
        };
    }

    private static UiBehaviorOptions Clone(UiBehaviorOptions src) => new()
    {
        MinimizeToTrayOnClose = src.MinimizeToTrayOnClose
    };
}
