using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

public sealed class BarcodeGenSettingsStore(IAppConfigStore configStore) : IBarcodeGenSettingsStore
{
    public BarcodeGenOptions Load()
        => BarcodeGenSettingsService.Normalize(configStore.Load().BarcodeGen);

    public Task SaveAsync(BarcodeGenOptions options, CancellationToken ct = default)
        => configStore.UpdateAsync(
            cfg => cfg.BarcodeGen = BarcodeGenSettingsService.Normalize(options),
            ct);
}
