using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

public sealed class AutomationConfigService : IAutomationConfigService
{
    private readonly IAppConfigStore _configStore;

    public AutomationConfigService(IAppConfigStore configStore)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    }

    public AutomationConfigDto Load()
        => AutomationContractMapper.ToApplication(_configStore.Load().AutomationTools);

    public async Task SaveAsync(AutomationConfigDto options, CancellationToken ct)
    {
        var contract = AutomationContractMapper.ToContract(options);
        await _configStore.UpdateAsync(cfg => cfg.AutomationTools = contract, ct).ConfigureAwait(false);
    }
}
