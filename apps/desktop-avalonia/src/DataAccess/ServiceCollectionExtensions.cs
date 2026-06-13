using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PacToolkits.Desktop.Avalonia.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgres(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<PgOptions>(config.GetSection("Postgres"));

        services.AddSingleton<IPgDataSourceFactory, PgDataSourceFactory>();

        services.AddSingleton<IDb, PgDb>();

        return services;
    }
}
