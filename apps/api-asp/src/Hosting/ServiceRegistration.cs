using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Hosting;

/// <summary>API 宿主 DI 组装入口</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPacToolkitsApi(this IServiceCollection services, IConfiguration config)
    {
        services.AddPacToolkitsAuth(config);
        return services;
    }
}
