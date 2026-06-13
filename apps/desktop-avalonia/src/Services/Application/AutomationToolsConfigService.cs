using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed class AutomationToolsConfigService : IAutomationToolsConfigService
{
    private readonly IAppConfigStore _configStore;

    public AutomationToolsConfigService(IAppConfigStore configStore)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    }

    public AutomationToolsOptions Load()
        => _configStore.Load().AutomationTools;

    public async Task SaveAsync(AutomationToolsOptions options, CancellationToken ct)
    {
        var cfg = _configStore.Load();
        cfg.AutomationTools = options;
        await _configStore.SaveAsync(cfg, ct).ConfigureAwait(false);
    }
}
