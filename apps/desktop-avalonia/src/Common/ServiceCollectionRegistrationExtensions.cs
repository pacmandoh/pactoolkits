using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.DataAccess;
using PacToolkits.Desktop.Avalonia.Repositories;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Integration;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class ServiceCollectionRegistrationExtensions
{
    public static IServiceCollection AddPacToolkitsUiServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddCoreInfrastructure(config);
        services.AddUiShell();
        services.AddApplicationServices();
        services.AddDataAccessServices();
        services.AddPageViewModels();
        services.AddPostgres(config);
        return services;
    }

    private static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IAppConfigStore, AppConfigStore>();
        services.AddSingleton<IUiBehaviorService, UiBehaviorService>();
        services.AddSingleton<ILoggingSettingsService, LoggingSettingsService>();
        services.AddSingleton<IUpdateSettingsService, UpdateSettingsService>();
        services.AddSingleton<IDbConfigService, DbConfigService>();
        services.AddSingleton<ITraceCodeRuleService, TraceCodeRuleService>();
        services.AddSingleton<IDbConnectionTester, DbConnectionTester>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<IReleaseVersionService, ReleaseVersionService>();
        services.AddSingleton<IAppStartupStateService, AppStartupStateService>();
        services.AddSingleton<IDbSchemaVersionService, DbSchemaVersionService>();
        services.AddSingleton<IDbSchemaMigrationService, DbSchemaMigrationService>();
        services.AddSingleton<ITraceEntryLogService, TraceEntryLogService>();
        services.AddSingleton<IDbConnectionMonitorService, DbConnectionMonitorService>();
        services.AddSingleton<IChangeWatermarkService, ChangeWatermarkService>();
        return services;
    }

    private static IServiceCollection AddUiShell(this IServiceCollection services)
    {
        services.AddSingleton<PageNavigationService>();
        services.AddSingleton<AppViews>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<SukiToastManager>();
        services.AddSingleton<ISukiToastManager>(sp => sp.GetRequiredService<SukiToastManager>());
        services.AddSingleton<SukiDialogManager>();
        services.AddSingleton<ISukiDialogManager>(sp => sp.GetRequiredService<SukiDialogManager>());
        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISensitiveOperationUnlockService, SensitiveOperationUnlockService>();
        services.AddSingleton<IAhkRuntimeService, AhkRuntimeService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IUpdateUiFlowService, UpdateUiFlowService>();
        services.AddSingleton<IMsfxApiClient, MsfxApiClient>();
        return services;
    }

    private static IServiceCollection AddDataAccessServices(this IServiceCollection services)
    {
        services.AddSingleton<IDrugIndexRepo, DrugIndexRepo>();
        services.AddSingleton<IScanCodeRepo, ScanCodeRepo>();
        services.AddSingleton<IDashboardRepo, DashboardRepo>();
        services.AddSingleton<IInventoryOverviewRepo, InventoryOverviewRepo>();
        services.AddSingleton<IClientIdReadRepo, ClientIdReadRepo>();
        services.AddSingleton<IMsfxSyncRepo, MsfxSyncRepo>();
        services.AddSingleton<ILookupCatalogService, LookupCatalogService>();
        services.AddSingleton<ClientAliasStore>();
        services.AddSingleton<IClientAliasService, ClientAliasService>();
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
            services.AddSingleton(typeof(AppPageBase), sp => (AppPageBase)sp.GetRequiredService(t));
        }

        return services;
    }
}

