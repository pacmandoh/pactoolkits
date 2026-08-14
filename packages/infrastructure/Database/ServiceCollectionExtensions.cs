using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Repositories;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 注册 Infrastructure 层 PostgreSQL 服务和仓储实现
///
/// 由 API 的 DI 组装入口调用
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPacToolkitsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<PgOptions>(config.GetSection("Postgres"));

        services.AddSingleton<IPgDataSourceFactory, PgDataSourceFactory>();
        services.AddSingleton<IDb, PgDb>();

        services.AddSingleton<IDbConfigService, DbConfigService>();
        services.AddSingleton<IDbSchemaVersionService, DbSchemaVersionService>();
        services.AddSingleton<IChangeWatermarkRepo, ChangeWatermarkRepo>();
        services.AddSingleton<ITraceEntryLogService, TraceEntryLogService>();

        services.AddSingleton<IDrugIndexRepo, DrugIndexRepo>();
        services.AddSingleton<IScanCodeRepo, ScanCodeRepo>();
        services.AddSingleton<IDashboardRepo, DashboardRepo>();
        services.AddSingleton<IInventoryOverviewRepo, InventoryOverviewRepo>();
        services.AddSingleton<MsfxSyncRepo>();
        services.AddSingleton<IMsfxPullRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxIngestRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxMappingRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxInjectRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxQueryRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IInjectorRepo, InjectorRepo>();

        return services;
    }
}
