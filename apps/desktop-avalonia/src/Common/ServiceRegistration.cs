using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;
using PacToolkits.Desktop.Avalonia.Services.Integration;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Workspace;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using PacToolkits.Desktop.Avalonia.Views.Dialogs;
using PacToolkits.Infrastructure.Database;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>Desktop DI 组装入口：注册应用服务、基础设施实现和 UI 服务</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPacToolkitsUiServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddDesktopInfrastructure(config);
        services.AddPacToolkitsInfrastructure(config);
        services.AddPacToolkitsApplication();
        services.AddUiShell();
        services.AddDesktopMsfxUpdate();
        services.AddDesktopWorkspace();
        services.AddDesktopPresentation();
        services.AddDesktopAgents();
        services.AddPageViewModels();
        return services;
    }

    private static IServiceCollection AddDesktopInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
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
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDialogService, DialogService>();
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
            manager.Register<AppInfoView, AppInfo>();
            manager.Register<InfoDetailView, InfoDetail>();
            manager.Register<MsfxStateDetailView, MsfxStateDetail>();
            manager.Register<MsfxTaskSplitView, MsfxTaskSplit>();
            return manager;
        });
        services.AddSingleton<ToastManager>();
        return services;
    }

    private static IServiceCollection AddDesktopMsfxUpdate(this IServiceCollection services)
    {
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IMsfxApiClient, MsfxApiClient>();
        return services;
    }

    private static IServiceCollection AddDesktopWorkspace(this IServiceCollection services)
    {
        services.AddSingleton<WorkspaceDirtyRefresh>();
        return services;
    }

    private static IServiceCollection AddDesktopPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        services.AddSingleton<ISensitiveUnlockService, SensitiveUnlockService>();
        services.AddSingleton<UnlockActivity>();
        services.AddSingleton<IUpdateFlowService, UpdateFlowService>();
        return services;
    }

    private static IServiceCollection AddDesktopAgents(this IServiceCollection services)
    {
        services.AddSingleton<AgentsRuntime>();
        services.AddSingleton<IAgentsRuntime>(sp => sp.GetRequiredService<AgentsRuntime>());
        services.AddSingleton<IAgentsConfigService, AgentsConfigService>();
        services.AddSingleton<IModuleSettingsStore, ModuleSettingsStore>();
        services.AddSingleton<IAgentsManager, AgentsManager>();
        return services;
    }

    internal static IServiceCollection AddPageViewModels(this IServiceCollection services)
    {
        return services
            .AddAppPage<Dashboard>()
            .AddAppPage<InventoryOverview>()
            .AddAppPage<DrugIndex>()
            .AddAppPage<ScanCode>()
            .AddAppPage<MsfxLink>()
            .AddAppPage<Settings>();
    }

    internal static IServiceCollection AddAppPage<TPage>(this IServiceCollection services)
        where TPage : AppPageBase
    {
        services.AddSingleton<TPage>();
        services.AddSingleton<AppPageBase>(sp => sp.GetRequiredService<TPage>());
        return services;
    }
}
