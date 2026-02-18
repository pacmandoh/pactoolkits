using System;
using System.Threading;
using System.Threading.Tasks;

namespace pactoolkits_ui.Services;

public interface IUiBehaviorService
{
    UiBehaviorOptions Current { get; }
    event Action? Changed;
    Task SaveAsync(UiBehaviorOptions options, CancellationToken ct = default);
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
        var cfg = _configStore.Load();
        cfg.UiBehavior = Clone(normalized);
        await _configStore.SaveAsync(cfg, ct).ConfigureAwait(false);

        _current = normalized;
        Changed?.Invoke();
    }

    public void Reload()
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
