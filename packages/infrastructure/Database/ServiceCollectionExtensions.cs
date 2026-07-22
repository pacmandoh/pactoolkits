using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Repositories;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// Infrastructure 层 DI 注册入口
///
/// 负责：Postgres 选项、数据库服务与各 Repo 的 Singleton 绑定
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
        services.AddSingleton<IChangeWatermarkService, ChangeWatermarkService>();
        services.AddSingleton<ITraceEntryLogService, TraceEntryLogService>();

        services.AddSingleton<IDrugIndexRepo, DrugIndexRepo>();
        services.AddSingleton<IScanCodeRepo, ScanCodeRepo>();
        services.AddSingleton<IDashboardRepo, DashboardRepo>();
        services.AddSingleton<IInventoryOverviewRepo, InventoryOverviewRepo>();
        services.AddSingleton<IClientIdReadRepo, ClientIdReadRepo>();
        services.AddSingleton<IMsfxSyncRepo, MsfxSyncRepo>();

        return services;
    }
}
