using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services.Msfx;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services;

/// <summary>
/// 注册 Application 层默认 Service 到 DI
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPacToolkitsApplication(this IServiceCollection services)
    {
        services.AddSingleton<IPinyinSearchCatalogCache, PinyinSearchCatalogCache>();
        services.AddSingleton<IDbAccessGuard, DbAccessGuard>();
        services.AddSingleton<IDbMigrationPolicyService, DbMigrationPolicyService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IInventoryOverviewService, InventoryOverviewService>();
        services.AddSingleton<IScanCodeService, ScanCodeService>();
        services.AddSingleton<IDrugIndexService, DrugIndexService>();
        services.AddSingleton<SyncService>();
        services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<SyncService>());
        services.AddSingleton<IMsfxAutoRunStore>(sp => sp.GetRequiredService<SyncService>());
        services.AddSingleton<IMsfxAutoRunService, MsfxAutoRunService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILookupCatalogService, LookupCatalogService>();
        services.AddSingleton<IClientAliasService, ClientAliasService>();
        services.AddSingleton<ITraceCodeRuleService, TraceCodeRuleService>();
        services.AddSingleton<IUpdateSettingsService, UpdateSettingsService>();
        services.AddSingleton<IReleaseChannelService, ReleaseChannelService>();
        services.AddSingleton<SensitiveUnlockSession>();
        return services;
    }
}
