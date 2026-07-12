using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;
using PacToolkits.Desktop.Avalonia.Services.Integration;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using PacToolkits.Desktop.Avalonia.Views.Dialogs;
using PacToolkits.Infrastructure.Database;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class ServiceRegistration
{
    public static IServiceCollection AddPacToolkitsUiServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddCoreInfrastructure(config);
        services.AddPacToolkitsInfrastructure(config);
        services.AddPacToolkitsApplication();
        services.AddUiShell();
        services.AddApplicationServices();
        services.AddPageViewModels();
        return services;
    }

    private static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<AppConfigStore>();
        services.AddSingleton<IAppConfigStore>(sp => sp.GetRequiredService<AppConfigStore>());
        services.AddSingleton<IDbOptionsStore>(sp => sp.GetRequiredService<AppConfigStore>());
        services.AddSingleton<IClientAliasStore, ClientAliasStore>();
        services.AddSingleton<ITraceCodeRuleStore, TraceCodeRuleStore>();
        services.AddSingleton<IUpdateSettingsStore, UpdateSettingsStore>();
        services.AddSingleton<IUiBehaviorService, UiBehaviorService>();
        services.AddSingleton<ILoggingSettingsService, LoggingSettingsService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<IReleaseVersionService, ReleaseVersionService>();
        services.AddSingleton<IAppStartupStateService, AppStartupStateService>();
        return services;
    }

    private static IServiceCollection AddUiShell(this IServiceCollection services)
    {
        services.AddSingleton<PageNavigationService>();
        services.AddSingleton<AppViews>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<SensitiveUnlock>();

        services.AddSingleton<DialogManager>(sp =>
        {
            var manager = new DialogManager();
            manager.Register<SensitiveUnlockView, SensitiveUnlock>();
            manager.Register<DrugKeyFixPreviewView, DrugKeyFixPreview>();
            manager.Register<InfoDetailView, InfoDetail>();
            manager.Register<MsfxStateDetailView, MsfxStateDetail>();
            manager.Register<MsfxMappingBatchView, MsfxMappingBatch>();
            manager.Register<MsfxTaskSplitView, MsfxTaskSplit>();
            return manager;
        });
        services.AddSingleton<ToastManager>();
        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<WorkspaceDirtyRefresh>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISensitiveUnlockService, SensitiveUnlockService>();
        services.AddSingleton<AhkInjectorAgentRuntime>();
        services.AddSingleton<IInjectorAgentRuntime>(sp => sp.GetRequiredService<AhkInjectorAgentRuntime>());
        services.AddSingleton<IAgentRuntime>(sp => sp.GetRequiredService<AhkInjectorAgentRuntime>());
        services.AddSingleton<IAgentManager, AgentManager>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IUpdateFlowService, UpdateFlowService>();
        services.AddSingleton<IMsfxApiClient, MsfxApiClient>();
        services.AddSingleton<IAutomationConfigService, AutomationConfigService>();
        return services;
    }

    private static IServiceCollection AddPageViewModels(this IServiceCollection services)
    {
        var asm = typeof(AppPageBase).Assembly;

        var pageTypes = asm.GetTypes()
            .Where(t => !t.IsAbstract && typeof(AppPageBase).IsAssignableFrom(t));

        foreach (var t in pageTypes)
        {
            services.AddSingleton(t);
            // Concrete page + one IEnumerable<AppPageBase> entry per page type.
            services.AddSingleton(typeof(AppPageBase), sp => (AppPageBase)sp.GetRequiredService(t));
        }

        return services;
    }
}
