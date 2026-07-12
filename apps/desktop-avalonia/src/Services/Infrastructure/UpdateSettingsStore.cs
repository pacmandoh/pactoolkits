using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public sealed class UpdateSettingsStore(IAppConfigStore configStore) : IUpdateSettingsStore
{
    public UpdateOptions Load()
        => configStore.Load().Update;

    public Task SaveAsync(UpdateOptions options, CancellationToken ct = default)
        => configStore.UpdateAsync(cfg => cfg.Update = options, ct);
}
