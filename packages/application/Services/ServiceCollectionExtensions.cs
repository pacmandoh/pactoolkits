using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPacToolkitsApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IInventoryOverviewService, InventoryOverviewService>();
        services.AddSingleton<IScanCodeService, ScanCodeService>();
        services.AddSingleton<IDrugIndexService, DrugIndexService>();
        services.AddSingleton<IMsfxSyncService, MsfxSyncService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILookupCatalogService, LookupCatalogService>();
        return services;
    }
}
