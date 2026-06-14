using System;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

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
        await _configStore.UpdateAsync(cfg => cfg.UiBehavior = Clone(normalized), ct).ConfigureAwait(false);

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
