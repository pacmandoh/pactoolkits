using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public interface IUiBehaviorService
{
    UiBehaviorOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(UiBehaviorOptions options, CancellationToken ct = default);
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
        LoadFromConfig();
    }

    public async Task SaveAsync(UiBehaviorOptions options, CancellationToken ct = default)
    {
        var normalized = Normalize(options);
        await _configStore.UpdateAsync(cfg => cfg.UiBehavior = Clone(normalized), ct).ConfigureAwait(false);

        _current = normalized;
        Changed?.Invoke();
    }

    private void LoadFromConfig()
    {
        var cfg = _configStore.Load();
        _current = Normalize(cfg.UiBehavior);
        Changed?.Invoke();
    }

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
