using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Repositories;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 注册 Infrastructure 层 PostgreSQL 服务和仓储实现
///
/// 由 Desktop / API 的 DI 组装入口调用
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPacToolkitsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PgOptions>(config.GetSection("Postgres"));

        services.AddSingleton<IPgDataSourceFactory, PgDataSourceFactory>();
        services.AddSingleton<IDb, PgDb>();

        services.AddSingleton<IDbConfigService, DbConfigService>();
        services.AddSingleton<IDbConfigNotifier, DbConfigNotifier>();
        services.AddSingleton<IDbConnectionTester, DbConnectionTester>();
        services.AddSingleton<IDbConnectionMonitorService, DbConnectionMonitorService>();
        services.AddSingleton<IDbSchemaVersionService, DbSchemaVersionService>();
        // Desktop 过渡：本机 LISTEN；IChangeWatermarkRepo 供 API watermarks 端点
        services.AddSingleton<IChangeWatermarkService, ChangeWatermarkService>();
        services.AddSingleton<IChangeWatermarkRepo, ChangeWatermarkRepo>();
        services.AddSingleton<ITraceEntryLogService, TraceEntryLogService>();

        services.AddSingleton<IDrugIndexRepo, DrugIndexRepo>();
        services.AddSingleton<IScanCodeRepo, ScanCodeRepo>();
        services.AddSingleton<IDashboardRepo, DashboardRepo>();
        services.AddSingleton<IInventoryOverviewRepo, InventoryOverviewRepo>();
        services.AddSingleton<IClientIdReadRepo, ClientIdReadRepo>();
        services.AddSingleton<MsfxSyncRepo>();
        services.AddSingleton<IMsfxPullRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxIngestRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxMappingRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxInjectRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());
        services.AddSingleton<IMsfxQueryRepo>(sp => sp.GetRequiredService<MsfxSyncRepo>());

        return services;
    }
}
